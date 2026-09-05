using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class DoubleJumpController : NetworkBehaviour, IModular
    {
        [Header("Settings")]
        public float JumpHeight = 1.0f;

        private ModularController _hub;
        private bool _canDoubleJump;

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

            if (_hub.IsGrounded)
            {
                _canDoubleJump = true;
            }
            else if (player.jump && _canDoubleJump)
            {
                float jumpForce = Mathf.Sqrt(JumpHeight * -2f * _hub.BaseGravity);
                _hub.VerticalVelocity = jumpForce;
                _canDoubleJump = false;
                player.jump = false; // Consume input
            }
        }
    }
}
