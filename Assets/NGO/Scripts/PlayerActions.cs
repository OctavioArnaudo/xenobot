using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerActions : NetworkBehaviour
{
    [SerializeField] private GameObject projectilePrefab;

    void Update()
    {
        // Only the owner of this player object can trigger the action
        if (!IsOwner) return;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            FireServerRpc();
        }
#else
        if (Input.GetKeyDown(KeyCode.Space))
        {
            FireServerRpc();
        }
#endif
    }

    [Rpc(SendTo.Server)]
    private void FireServerRpc()
    {
        // Logic executed on the server
        Debug.Log($"Player {OwnerClientId} is firing!");

        // In a real scenario, you would spawn a projectile here:
        // GameObject go = Instantiate(projectilePrefab, transform.position, transform.rotation);
        // go.GetComponent<NetworkObject>().Spawn();

        // Notify all clients that someone fired (e.g., to play a sound or particle effect)
        NotifyFiredClientRpc(OwnerClientId);
    }

    [Rpc(SendTo.Everyone)]
    private void NotifyFiredClientRpc(ulong playerId)
    {
        if (IsOwner) return; // We already know we fired

        Debug.Log($"Client: Player {playerId} fired a shot!");
    }
}
