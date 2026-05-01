using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerInputHandler : NetworkBehaviour
{
    void Update()
    {
        if (!IsOwner) return;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.qKey.wasPressedThisFrame) SendAction(0);
            if (Keyboard.current.eKey.wasPressedThisFrame) SendAction(1);

            // Test Crafting: Press C to craft item 100
            if (Keyboard.current.cKey.wasPressedThisFrame)
            {
                if (CraftingService.Instance != null)
                    CraftingService.Instance.RequestCraftRpc(100);
            }

            // Test Materials: Press G to get materials (Server-side simulation)
            if (Keyboard.current.gKey.wasPressedThisFrame)
            {
                RequestMaterialsServerRpc();
            }
        }
#endif
    }

    [Rpc(SendTo.Server)]
    private void RequestMaterialsServerRpc(RpcParams rpcParams = default)
    {
        if (TryGetComponent<InventoryState>(out var inv))
        {
            inv.AddItem(1, 2); // Material 1
            inv.AddItem(2, 1); // Material 2
            Debug.Log("[Server] Added materials to player " + rpcParams.Receive.SenderClientId);
        }
    }

    private void SendAction(int type)
    {
        if (CombatService.Instance != null)
        {
            CombatService.Instance.ExecuteActionRpc(type, transform.position, OwnerClientId);
        }
        else
        {
            Debug.LogWarning("CombatService Instance not found!");
        }
    }
}
