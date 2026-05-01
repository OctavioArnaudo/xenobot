using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerActionReceiver : NetworkBehaviour
{
    // Feedback visuals for actions
    [SerializeField] private ParticleSystem hitEffect;
    [SerializeField] private ParticleSystem healEffect;

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ApplyNetworkEffectServerRpc(int type, ulong fromId)
    {
        // Server-side logic to update stats
        if (TryGetComponent<PlayerStats>(out var stats))
        {
            switch (type)
            {
                case 0: // Damage
                    stats.TakeDamageServerRpc(20);
                    NotifyEffectClientRpc(0);
                    break;
                case 1: // Heal
                    stats.Health.Value += 15;
                    NotifyEffectClientRpc(1);
                    break;
            }
        }
        Debug.Log($"Player {OwnerClientId} affected by type {type} from {fromId}");
    }

    [Rpc(SendTo.Everyone)]
    private void NotifyEffectClientRpc(int type)
    {
        // Visual feedback on all clients
        switch (type)
        {
            case 0:
                if (hitEffect) hitEffect.Play();
                Debug.Log("Ouch! Hit!");
                break;
            case 1:
                if (healEffect) healEffect.Play();
                Debug.Log("Feeling better! Healed!");
                break;
        }
    }
}
