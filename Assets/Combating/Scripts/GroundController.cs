using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    [DefaultExecutionOrder(-50)]
    public class GroundController : MonoBehaviour, IModular
    {
        // Physical Reliability Constants (Hardcoded to prevent inspector tampering)
        private const float GroundedOffset = 0.14f;
        private const float GroundedRadius = 0.28f;
        private const float GroundStickVelocity = -2f;
        private const int GroundLayers = ~0; // Everything

        [Header("Audio Settings")]
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        private CharacterController _controller;
        private ModularController _hub;
        private Collider[] _groundHits = new Collider[8];

        private bool HasPhysicsAuthority => _hub != null && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || _hub.IsOwner);

        private void Awake()
        {
            // Hub will call Bind() during assembly.
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
        }

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
            // Position the check sphere slightly above the feet, extending downwards
            Vector3 checkPos = _hub.transform.position + Vector3.up * GroundedOffset;
            _hub.IsGrounded = _controller.isGrounded || HasExternalGroundHit(checkPos);
        }

        private bool HasExternalGroundHit(Vector3 checkPos)
        {
            int layerMask = GroundLayers & ~(1 << _hub.gameObject.layer);
            int hitCount = Physics.OverlapSphereNonAlloc(checkPos, GroundedRadius, _groundHits, layerMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _groundHits[i];
                _groundHits[i] = null;

                if (hit == null || hit.transform.IsChildOf(_hub.transform)) continue;
                return true;
            }

            return false;
        }

        private void ApplyGravity()
        {
            if (_hub.IsGrounded)
            {
                if (_hub.VerticalVelocity < 0) _hub.VerticalVelocity = GroundStickVelocity;
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
