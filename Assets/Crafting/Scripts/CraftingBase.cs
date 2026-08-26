using Unity.Netcode;
using UnityEngine;

namespace NGO.Gameplay.Base
{
    /// <summary>
    /// Script hipotético/base que define la estructura de crafting.
    /// Se queda solo con la definición de red.
    /// </summary>
    public abstract class CraftingBase : NetworkBehaviour
    {
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public virtual void RequestCraftRpc(int itemIDToCraft, RpcParams rpcParams = default) { }
    }
}
