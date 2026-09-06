using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Source of Truth for all entity animations.
    /// Synchronizes Animator parameters based on Hub physical state.
    /// </summary>
    public class AnimationController : MonoBehaviour, IModular
    {
        private Animator _animator;
        private ModularController _hub;

        // Parameter Hashes for performance
        private static readonly int _animIDSpeed = Animator.StringToHash("Speed");
        private static readonly int _animIDIsGrounded = Animator.StringToHash("isGrounded");
        private static readonly int _animIDVerticalVelocity = Animator.StringToHash("VerticalVelocity");
        private static readonly int _animIDJump = Animator.StringToHash("Jump");
        private static readonly int _animIDTakeDamage = Animator.StringToHash("takeDamage");
        private static readonly int _animIDMeleeAttack = Animator.StringToHash("meleeAttack");
        private static readonly int _animIDShoot = Animator.StringToHash("shoot");

        private bool _hasAnimIDSpeed;
        private bool _hasAnimIDIsGrounded;
        private bool _hasAnimIDVerticalVelocity;
        private bool _hasAnimIDJump;
        private bool _hasAnimIDTakeDamage;
        private bool _hasAnimIDMeleeAttack;
        private bool _hasAnimIDShoot;

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
                _animator = _hub.animator;
                AssignAnimationIDs();
            }
        }

        private void AssignAnimationIDs()
        {
            if (_animator == null) return;

            _hasAnimIDSpeed = HasParameter(_animator, _animIDSpeed);
            _hasAnimIDIsGrounded = HasParameter(_animator, _animIDIsGrounded);
            _hasAnimIDVerticalVelocity = HasParameter(_animator, _animIDVerticalVelocity);
            _hasAnimIDJump = HasParameter(_animator, _animIDJump);
            _hasAnimIDTakeDamage = HasParameter(_animator, _animIDTakeDamage);
            _hasAnimIDMeleeAttack = HasParameter(_animator, _animIDMeleeAttack);
            _hasAnimIDShoot = HasParameter(_animator, _animIDShoot);
        }

        private void Update()
        {
            if (_hub == null || _animator == null || !_animator.enabled) return;

            // Sync with physical state from Hub
            if (_hasAnimIDSpeed) _animator.SetFloat(_animIDSpeed, _hub.HorizontalSpeed);
            if (_hasAnimIDIsGrounded) _animator.SetBool(_animIDIsGrounded, _hub.IsGrounded);
            if (_hasAnimIDVerticalVelocity) _animator.SetFloat(_animIDVerticalVelocity, _hub.VerticalVelocity);

            // Handle Jump trigger if Hub is Player and jump was just pressed
            if (_hub is PlayerController player && player.jump && _hasAnimIDJump)
            {
                _animator.SetBool(_animIDJump, true);
            }
        }

        public void TriggerTakeDamage()
        {
            if (_animator != null && _hasAnimIDTakeDamage) _animator.SetTrigger(_animIDTakeDamage);
        }

        public void TriggerMeleeAttack()
        {
            if (_animator != null && _hasAnimIDMeleeAttack) _animator.SetTrigger(_animIDMeleeAttack);
        }

        public void TriggerShoot()
        {
            if (_animator != null && _hasAnimIDShoot) _animator.SetTrigger(_animIDShoot);
        }

        private bool HasParameter(Animator animator, int paramHash)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            foreach (AnimatorControllerParameter param in animator.parameters)
                if (param.nameHash == paramHash) return true;
            return false;
        }
    }
}
