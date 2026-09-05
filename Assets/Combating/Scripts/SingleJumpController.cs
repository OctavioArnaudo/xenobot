using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class SingleJumpController : NetworkBehaviour, IModular
    {
        [Header("Settings")]
        public float JumpHeight = 1.2f;

        private ModularController _hub;

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);
            }
        }

        public void OnRefreshModule() { }

        private void Update()
        {
            if (_hub == null || !(_hub is PlayerController player)) return;

            bool isNetworkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (isNetworkActive && !player.IsOwner) return;

            if (player.jump && _hub.IsGrounded)
            {
                float jumpForce = Mathf.Sqrt(JumpHeight * -2f * _hub.BaseGravity);
                _hub.VerticalVelocity = jumpForce;
                player.jump = false; // Consume input
            }
        }
    }
}
