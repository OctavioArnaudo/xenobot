using UnityEngine;
using Unity.Netcode;
using NGO.Gameplay.Base;
using StarterAssets;

namespace NGO.Gameplay.Networking
{
    public class JumpPlayerController : PlayerActionController
    {
        public float JumpHeight = 5.0f;
        public float JumpTimeout = 0.05f;
        private float _jumpTimeoutDelta;

        private StarterAssetsInputs _input;
        private ApplyGravityController _gravityController;
        private Animator _animator;
        private int _animIDJump;

        public override void Initialize(Unity.Netcode.NetworkObject root)
        {
            base.Initialize(root);
            _input = root.GetComponentInChildren<StarterAssetsInputs>();
            _animator = root.GetComponentInChildren<Animator>();

            _gravityController = root.GetComponentInChildren<ApplyGravityController>();

            _animIDJump = Animator.StringToHash("Jump");
        }

        public override void OnActionTriggered() { }

        public override void OnTick()
        {
            if (!IsOwner || _input == null || _gravityController == null) return;

            if (_gravityController.Grounded)
            {
                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    _gravityController.VerticalVelocity = Mathf.Sqrt(JumpHeight * -2f * _gravityController.Gravity);
                    if (_animator != null) _animator.SetBool(_animIDJump, true);
                }

                if (_jumpTimeoutDelta >= 0.0f) _jumpTimeoutDelta -= Time.deltaTime;
            }
            else
            {
                _jumpTimeoutDelta = JumpTimeout;
                if (_animator != null) _animator.SetBool(_animIDJump, false);
                _input.jump = false;
            }
        }
    }
}
