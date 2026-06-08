using Unity.Netcode;
using UnityEngine;

namespace NGO.Gameplay.Base
{
    /// <summary>
    /// Base para controladores de DATOS o ESTADO.
    /// </summary>
    public abstract class PlayerDataController : NetworkBehaviour
    {
        protected NetworkObject playerRoot;

        public virtual void Initialize(NetworkObject root)
        {
            playerRoot = root;
        }
    }
}
