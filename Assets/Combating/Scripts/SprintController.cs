using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for Sprint logic.
    /// Communicates with the hub to set sprint state.
    /// </summary>
    public class SprintController : NetworkBehaviour
    {
        private PlayerController _hub;

        private void Awake()
        {
            _hub = GetComponent<PlayerController>();
        }

        // The hub already updates sprint state from PlayerInput.
        // This script can be used for extra logic like stamina or visual effects.
    }
}
