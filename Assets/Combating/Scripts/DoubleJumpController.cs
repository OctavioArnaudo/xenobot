using UnityEngine;
using Unity.Netcode;

namespace Combating.Scripts
{
    public class DoubleJumpController : MonoBehaviour, IModular
    {
        private const float JumpHeight = 1.0f;
        private ModularController _hub;
        private bool _canDoubleJump;

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
            if (_hub == null || !(_hub is Testing.Scripts.PlayerController player)) return;

            bool isOwner = (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || _hub.IsOwner);
            if (!isOwner) return;

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
