using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for Landing audio and visual effects.
    /// </summary>
    public class LandingController : NetworkBehaviour, IPlayerModule
    {
        [Header("Audio Settings")]
        public AudioClip LandingAudioClip;
        [Range(0, 1)] public float LandingAudioVolume = 0.5f;

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

        private void OnLand(AnimationEvent animationEvent)
        {
            if (_controller == null) return;
            if (animationEvent.animatorClipInfo.weight > 0.5f && LandingAudioClip != null)
                AudioSource.PlayClipAtPoint(LandingAudioClip, transform.TransformPoint(_controller.center), LandingAudioVolume);
        }
    }
}
