using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Modular component to enable/configure basic jumping.
    /// </summary>
    public class SingleJumpController : NetworkBehaviour, IPlayerModule
    {
        private PlayerController _hub;

        private void Awake()
        {
            _hub = GetComponentInParent<PlayerController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(PlayerController hub)
        {
            _hub = hub;
            if (_hub != null) _hub.RegisterModule(this);
        }

        public void OnRefreshModule() { }
    }
}
