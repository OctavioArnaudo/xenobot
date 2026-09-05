using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for Ground-related audio and visual effects (e.g. Footsteps).
    /// </summary>
    public class GroundController : NetworkBehaviour, IPlayerModule
    {
        [Header("Audio Settings")]
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        private CharacterController _controller;
        private PlayerController _hub;

        private void Awake()
        {
            _hub = GetComponentInParent<PlayerController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(PlayerController hub)
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
