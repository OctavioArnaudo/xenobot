using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class CraftingService : NetworkBehaviour
{
    public static CraftingService Instance;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    // Definición simple de receta: ID Material -> Cantidad Requerida
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestCraftRpc(int itemIDToCraft, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        // Buscamos el inventario del jugador que hizo la petición
        NetworkObject playerObject = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
        if (playerObject == null) return;

        if (playerObject.TryGetComponent<InventoryState>(out var inventory))
        {
            if (CanCraft(inventory, itemIDToCraft))
            {
                ExecuteCraft(inventory, itemIDToCraft);
                Debug.Log($"[Server] Client {clientId} crafted item {itemIDToCraft}");
            }
            else
            {
                Debug.Log($"[Server] Client {clientId} failed to craft item {itemIDToCraft}: Missing materials.");
            }
        }
    }

    private bool CanCraft(InventoryState inv, int resultID)
    {
        // Lógica de ejemplo: El item 100 requiere 2 del item 1 y 1 del item 2
        if (resultID == 100)
        {
            return inv.HasItems(1, 2) && inv.HasItems(2, 1);
        }
        return false;
    }

    private void ExecuteCraft(InventoryState inv, int resultID)
    {
        if (resultID == 100)
        {
            inv.RemoveItem(1, 2);
            inv.RemoveItem(2, 1);
            inv.AddItem(100, 1);
        }
    }
}
