using Unity.Netcode;
using UnityEngine;

namespace NGO.Gameplay.Base
{
    public abstract class PlayerActionController : NetworkBehaviour
    {
        protected NetworkObject playerRoot;
        public bool isEnabled = true;

        public virtual void Initialize(NetworkObject root)
        {
            playerRoot = root;
            Debug.Log($"[Controller] {gameObject.name} inicializado.");
        }

        public abstract void OnActionTriggered();
        public virtual void OnActionReleased() { }
        public virtual void OnTick() { }
    }
}
