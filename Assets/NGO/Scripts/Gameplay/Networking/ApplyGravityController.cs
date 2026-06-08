using UnityEngine;
using Unity.Netcode;
using NGO.Gameplay.Base;

namespace NGO.Gameplay.Networking
{
    public class ApplyGravityController : PlayerActionController
    {
        public float Gravity = -25.0f;
        public float GroundedOffset = -0.14f;
        public float GroundedRadius = 0.28f;
        public LayerMask GroundLayers;

        [HideInInspector] public float VerticalVelocity;
        [HideInInspector] public bool Grounded = true;

        private CharacterController _controller;
        private Animator _animator;
        private bool _hasAnimator;

        private int _animIDGrounded;
        private int _animIDFreeFall;

        public override void Initialize(Unity.Netcode.NetworkObject root)
        {
            base.Initialize(root);
            _controller = root.GetComponent<CharacterController>();
            _animator = root.GetComponentInChildren<Animator>();
            _hasAnimator = _animator != null;

            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
        }

        public override void OnActionTriggered() { }

        public override void OnTick()
        {
            if (!IsOwner) return;

            GroundedCheck();
            ApplyVerticalForces();
        }

        private void GroundedCheck()
        {
            Vector3 spherePosition = new Vector3(playerRoot.transform.position.x, playerRoot.transform.position.y - GroundedOffset, playerRoot.transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
            if (_hasAnimator) _animator.SetBool(_animIDGrounded, Grounded);
        }

        private void ApplyVerticalForces()
        {
            if (Grounded && VerticalVelocity < 0.0f) VerticalVelocity = -2f;

            VerticalVelocity += Gravity * Time.deltaTime;
            _controller.Move(new Vector3(0.0f, VerticalVelocity, 0.0f) * Time.deltaTime);

            if (_hasAnimator && !Grounded && VerticalVelocity < -1.0f) _animator.SetBool(_animIDFreeFall, true);
            else if (_hasAnimator && Grounded) _animator.SetBool(_animIDFreeFall, false);
        }
    }
}
