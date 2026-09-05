using UnityEngine;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for Landing audio and visual effects.
    /// </summary>
    public class LandingController : NetworkBehaviour
    {
        [Header("Audio Settings")]
        public AudioClip LandingAudioClip;
        [Range(0, 1)] public float LandingAudioVolume = 0.5f;

        private CharacterController _controller;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f && LandingAudioClip != null)
                AudioSource.PlayClipAtPoint(LandingAudioClip, transform.TransformPoint(_controller.center), LandingAudioVolume);
        }
    }
}
