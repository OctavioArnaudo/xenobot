using Unity.Netcode;
using UnityEngine;

public class NetworkInteractable : NetworkBehaviour
{
    // A simple state synced across all clients
    public NetworkVariable<bool> IsToggled = new NetworkVariable<bool>(false);

    public void Interact()
    {
        // This method would be called by a player script locally
        ToggleServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleServerRpc(RpcParams rpcParams = default)
    {
        IsToggled.Value = !IsToggled.Value;
        Debug.Log($"Object interacted by Client {rpcParams.Receive.SenderClientId}. New state: {IsToggled.Value}");
    }

    public override void OnNetworkSpawn()
    {
        IsToggled.OnValueChanged += (oldVal, newVal) => {
            UpdateVisuals(newVal);
        };
        UpdateVisuals(IsToggled.Value);
    }

    private void UpdateVisuals(bool state)
    {
        // Change color or show/hide something based on state
        GetComponent<Renderer>().material.color = state ? Color.green : Color.red;
    }
}
