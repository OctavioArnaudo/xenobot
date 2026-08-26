using Unity.Netcode;
using UnityEngine;

namespace NGO.Gameplay.Base
{
    public abstract class InventoryBase : NetworkBehaviour
    {
        // En un sistema real usaríamos NetworkList<ItemData>
        [Rpc(SendTo.Server)]
        public virtual void AddItemRpc(int itemId, int quantity) { }

        [Rpc(SendTo.Server)]
        public virtual void RemoveItemRpc(int itemId, int quantity) { }
    }
}
