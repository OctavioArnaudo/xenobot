using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for Sprint logic.
    /// Communicates with the hub to set sprint state.
    /// </summary>
    public class SprintController : NetworkBehaviour, IPlayerModule
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

        // The hub already updates sprint state from PlayerInput.
        // This script can be used for extra logic like stamina or visual effects.
    }
}
