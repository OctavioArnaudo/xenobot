using Unity.Netcode;
using UnityEngine;

public class CombatService : NetworkBehaviour
{
    public static CombatService Instance;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ExecuteActionRpc(int type, Vector3 origin, ulong instigatorId)
    {
        float radius = 5f;
        Collider[] hits = Physics.OverlapSphere(origin, radius);

        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<NetworkHealth>(out var health))
            {
                ApplyEffect(type, health, instigatorId);
            }
        }
    }

    private void ApplyEffect(int type, NetworkHealth target, ulong instigator)
    {
        switch (type)
        {
            case 0: // Attack
                if (target.OwnerClientId == instigator) return;
                target.ModifyHealth(-20);
                break;
            case 1: // Heal
                target.ModifyHealth(15);
                break;
        }

        if (target.TryGetComponent<PlayerVisuals>(out var visuals))
        {
            visuals.PlayEffectRpc(type);
        }
    }
}
