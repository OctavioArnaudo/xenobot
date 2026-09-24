using UnityEngine;
using Unity.Netcode;

namespace Combating.Scripts
{
    public class DoubleJumpController : MonoBehaviour, IModular
    {
        private const float JumpHeight = 1.0f;
        private ModularController _hub;

        void Awake()
        {
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null) _hub.RegisterModule(this);
        }

        public void OnRefreshModule() { }

        private void Update()
        {
            bool isOwner = (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || _hub.IsOwner);
            if (!isOwner) return;

        }
    }
}
