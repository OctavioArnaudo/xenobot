using UnityEngine;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for Ground-related audio and visual effects (e.g. Footsteps).
    /// </summary>
    public class GroundController : NetworkBehaviour
    {
        [Header("Audio Settings")]
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        private CharacterController _controller;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f && FootstepAudioClips != null && FootstepAudioClips.Length > 0)
            {
                var index = Random.Range(0, FootstepAudioClips.Length);
                AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
            }
        }
    }
}
