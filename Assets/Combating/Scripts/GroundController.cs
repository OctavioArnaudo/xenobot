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
    // Smaller stick velocity to avoid forcing the CharacterController deep into the ground
    private const float GroundStickVelocity = -0.5f;
    private const int GroundLayers = ~0; // Everything

        [Header("Audio Settings")]
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        private CharacterController _controller;
        private ModularController _hub;
        private Collider[] _groundHits = new Collider[8];

        [Header("Debug")]
        public bool debugGroundChecks = false;

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



        private bool HasExternalGroundHit(Vector3 checkPos)
        {
            // Use full layers mask; ignore self-colliders later by IsChildOf checks.
            // Excluding the hub layer can hide valid ground colliders when player and ground share a layer.
            int layerMask = GroundLayers;
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

        private void UpdateGroundedState()
        {
            // AGGRESSIVE FIX: Ignore ground check if moving upwards significantly
            if (_hub.VerticalVelocity > 0.1f)
            {
                _hub.IsGrounded = false;
                return;
            }

            // UNBREAKABLE SPHERE: Position exactly at the base
            Vector3 spherePos = _hub.transform.position + Vector3.up * GroundedRadius;

            LayerMask mask = GroundLayers;
            // Robust fallback if layers are not configured
            if (mask == 0) mask = ~((1 << 3) | (1 << 2));

            // Combine Physics Sphere with Controller's own state for maximum reliability
            _hub.IsGrounded = Physics.CheckSphere(spherePos, GroundedRadius * 1.1f, mask, QueryTriggerInteraction.Ignore)
                             || (_controller != null && _controller.isGrounded);

            if (debugGroundChecks)
            {
                Debug.DrawLine(spherePos, spherePos + Vector3.down * GroundedRadius, _hub.IsGrounded ? Color.green : Color.red);
            }
        }

        private void ApplyGravity()
        {
            if (_hub.IsGrounded)
            {
                if (_hub.VerticalVelocity <= 0.01f)
                {
                    // AGGRESSIVE GLUE: Strong negative force to prevent floating and keep entity grounded
                    _hub.VerticalVelocity = -8.0f;
                }
            }
            else
            {
                // Apply normal gravity when in air
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
