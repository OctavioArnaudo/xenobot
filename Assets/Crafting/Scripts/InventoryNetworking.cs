using UnityEngine;
using NGO.Gameplay.Base;
using Unity.Netcode;

namespace NGO.Gameplay.Networking
{
    public class InventoryNetworking : InventoryBase
    {
        public override void AddItemRpc(int itemId, int quantity)
        {
            Debug.Log($"[Inventory] Añadiendo {quantity} de {itemId} al servidor.");
        }

        public override void RemoveItemRpc(int itemId, int quantity)
        {
            Debug.Log($"[Inventory] Eliminando {quantity} de {itemId} del servidor.");
        }
    }
}
