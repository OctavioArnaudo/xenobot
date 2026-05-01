using Unity.Netcode;
using UnityEngine;

public class NetworkActionManager : NetworkBehaviour
{
    // A simple data structure to represent a "CRUD" action in the network
    public struct ActionData : INetworkSerializable, System.IEquatable<ActionData>
    {
        public ulong ActionId;
        public ulong InstigatorId;
        public Vector3 Position;
        public int ActionType; // 0: Attack, 1: Heal, 2: Buff, etc.

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ActionId);
            serializer.SerializeValue(ref InstigatorId);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref ActionType);
        }

        public bool Equals(ActionData other)
        {
            return ActionId == other.ActionId;
        }
    }

    // List of active actions synced to all clients (Read part of CRUD)
    public NetworkList<ActionData> ActiveActions;

    void Awake()
    {
        ActiveActions = new NetworkList<ActionData>();
    }

    // CREATE: User requests to start an action
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestActionServerRpc(int type, Vector3 pos, RpcParams rpcParams = default)
    {
        var newData = new ActionData
        {
            ActionId = (ulong)System.Guid.NewGuid().GetHashCode(),
            InstigatorId = rpcParams.Receive.SenderClientId,
            Position = pos,
            ActionType = type
        };
        ActiveActions.Add(newData);

        // Execute the effect on the server to affect other players
        ResolveActionImpact(newData);
    }

    // UPDATE/DELETE: Typically handled by the server logic
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void EndActionServerRpc(ulong actionId)
    {
        for (int i = 0; i < ActiveActions.Count; i++)
        {
            if (ActiveActions[i].Equals(new ActionData { ActionId = actionId }))
            {
                ActiveActions.RemoveAt(i);
                break;
            }
        }
    }

    private void ResolveActionImpact(ActionData data)
    {
        if (!IsServer) return;

        float radius = 5f;
        Collider[] hitColliders = Physics.OverlapSphere(data.Position, radius);

        Debug.Log($"[Server] Resolving Action Type {data.ActionType} at {data.Position}. Found {hitColliders.Length} colliders.");

        foreach (var hit in hitColliders)
        {
            if (hit.TryGetComponent<PlayerActionReceiver>(out var receiver))
            {
                // Ensure we don't hit the instigator (optional for attacks)
                if (data.ActionType == 0 && receiver.OwnerClientId == data.InstigatorId)
                {
                    Debug.Log($"[Server] Skipping instigator {data.InstigatorId} for attack.");
                    continue;
                }

                Debug.Log($"[Server] Applying effect to Player {receiver.OwnerClientId}");
                receiver.ApplyNetworkEffectServerRpc(data.ActionType, data.InstigatorId);
            }
            else
            {
                Debug.Log($"[Server] Hit object {hit.name} but it has no PlayerActionReceiver.");
            }
        }
    }
}
