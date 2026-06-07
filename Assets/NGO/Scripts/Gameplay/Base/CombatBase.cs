using Unity.Netcode;
using UnityEngine;

namespace NGO.Gameplay.Base
{
    /// <summary>
    /// Script hipotético/base que define la estructura de combate.
    /// Se queda solo con la definición de red.
    /// </summary>
    public abstract class CombatBase : NetworkBehaviour
    {
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public virtual void ExecuteActionRpc(int type, Vector3 origin, ulong instigatorId) { }
    }
}
