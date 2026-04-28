using Unity.Netcode;
using UnityEngine;

public class PlayerStats : NetworkBehaviour
{
    // NetworkVariable allows syncing a value across all clients.
    // By default, it's Server Write, Client Read.
    public NetworkVariable<int> Health = new NetworkVariable<int>(100);
    public NetworkVariable<int> Score = new NetworkVariable<int>(0);

    public override void OnNetworkSpawn()
    {
        // Subscribe to value changes to update UI or play effects locally
        Health.OnValueChanged += OnHealthChanged;

        if (IsServer)
        {
            Debug.Log($"Player {OwnerClientId} spawned with {Health.Value} HP");
        }
    }

    public override void OnNetworkDespawn()
    {
        Health.OnValueChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        Debug.Log($"Player {OwnerClientId} Health changed from {previousValue} to {newValue}");
    }

    [Rpc(SendTo.Server)]
    public void TakeDamageServerRpc(int damage)
    {
        // Only the server can modify a NetworkVariable with default permissions
        Health.Value -= damage;
        if (Health.Value < 0) Health.Value = 0;
    }
}
