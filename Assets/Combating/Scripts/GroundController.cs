using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class GroundController : NetworkBehaviour, IModular
    {
        [Header("Settings")]
        public LayerMask GroundLayers = 1;
        public float GroundedOffset = 0.15f;
        public float GroundedRadius = 0.25f;

        [Header("Audio Settings")]
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        private CharacterController _controller;
        private ModularController _hub;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool HasPhysicsAuthority => _hub != null && (!IsNetworkActive || _hub.IsOwner);

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);
                OnRefreshModule();
            }
        }

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                _controller = _hub.controller ?? _hub.GetComponent<CharacterController>();
            }
        }

        private void Update()
        {
            if (_hub == null || !HasPhysicsAuthority || _controller == null) return;

            UpdateGroundedState();
            ApplyGravity();
        }

        private void UpdateGroundedState()
        {
            Vector3 checkPos = _hub.transform.position + Vector3.up * GroundedOffset;
            int layerMask = GroundLayers.value & ~(1 << 3); // Ignore Player Layer
            _hub.IsGrounded = _controller.isGrounded || Physics.CheckSphere(checkPos, GroundedRadius, layerMask, QueryTriggerInteraction.Ignore);
        }

        private void ApplyGravity()
        {
            if (_hub.IsGrounded)
            {
                if (_hub.VerticalVelocity < 0) _hub.VerticalVelocity = -2f;
            }
            else
            {
                _hub.VerticalVelocity += _hub.BaseGravity * Time.deltaTime;
            }
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (_controller == null) return;
            if (animationEvent.animatorClipInfo.weight > 0.5f && FootstepAudioClips != null && FootstepAudioClips.Length > 0)
            {
                var index = Random.Range(0, FootstepAudioClips.Length);
                AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
            }
        }
    }
}
