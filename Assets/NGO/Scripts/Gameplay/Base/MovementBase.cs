using Unity.Netcode;
using UnityEngine;

namespace NGO.Gameplay.Base
{
    /// <summary>
    /// Definición base para el movimiento en red.
    /// </summary>
    public abstract class MovementBase : NetworkBehaviour
    {
        public float Speed = 5f;

        [Rpc(SendTo.Server)]
        public virtual void RequestMoveRpc(Vector3 direction) { }
    }
}
