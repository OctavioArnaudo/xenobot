using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class MeleeController : NetworkBehaviour, IModular
    {
        [Header("Settings")]
        public float attackRange = 2.5f;
        public float attackDamage = 35f;
        public float attackCooldown = 1f;
        public LayerMask targetLayers;

        [Header("Visuals")]
        public ProjectileController swingVfxPrefab;
        public Renderer[] visualsToRotate;
        public float rotationSpeed = 10f;

        [Header("Animation")]
        public Animator animator;

        private HealthController m_Health;
        private ModularController _hub;
        private float m_NextAttackTime;

        private static readonly int _animIDMeleeAttack = Animator.StringToHash("meleeAttack");
        private bool _hasAnimator;
        private bool _hasAnimIDMeleeAttack;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        void Awake()
        {
            _hub = GetComponentInParent<ModularController>();
            if (_hub != null) Bind(_hub);

            if (visualsToRotate == null || visualsToRotate.Length == 0)
                visualsToRotate = GetComponentsInChildren<Renderer>();

            RefreshAnimatorReference();
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
                m_Health = _hub.GetModule<HealthController>();
                visualsToRotate = _hub.renderRoot?.GetComponentsInChildren<Renderer>() ?? GetComponentsInChildren<Renderer>();
            }
            RefreshAnimatorReference();
        }

        private void RefreshAnimatorReference()
        {
            if (animator == null)
            {
                animator = (_hub != null) ? _hub.animator : GetComponentInChildren<Animator>();
            }
            _hasAnimator = animator != null;
            if (_hasAnimator) _hasAnimIDMeleeAttack = HasParameter(animator, _animIDMeleeAttack);
        }

        private void Update()
        {
            if (_hub != null && _hub is PlayerController player && player.fire && Time.time >= m_NextAttackTime)
            {
                PerformMeleeAction();
                player.fire = false;
            }
        }

        public void PerformMeleeAction(Vector3? targetPosition = null)
        {
            if (Time.time < m_NextAttackTime) return;

            if (targetPosition.HasValue)
            {
                RotateVisualsTowards(targetPosition.Value);
            }

            m_NextAttackTime = Time.time + attackCooldown;
            TriggerMeleeAnimation();

            if (IsNetworkActive)
            {
                if (IsOwner) RequestMeleeServerRpc();
            }
            else
            {
                ExecuteMelee();
            }
        }

        private void TriggerMeleeAnimation()
        {
            if (!_hasAnimator || animator == null)
            {
                RefreshAnimatorReference();
                if (!_hasAnimator || animator == null) return;
            }
            if (_hasAnimIDMeleeAttack) animator.SetTrigger(_animIDMeleeAttack);
        }

        private void RotateVisualsTowards(Vector3 targetPosition)
        {
            if (visualsToRotate == null) return;
            Vector3 direction = (targetPosition - transform.position).normalized;
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetFullRotation = Quaternion.LookRotation(direction);
                foreach (var r in visualsToRotate)
                {
                    if (r != null)
                        r.transform.rotation = Quaternion.Slerp(r.transform.rotation, targetFullRotation, rotationSpeed * Time.deltaTime);
                }
            }
        }

        [ServerRpc]
        private void RequestMeleeServerRpc()
        {
            ExecuteMelee();
        }

        private void ExecuteMelee()
        {
            float finalDamage = attackDamage;
            var stats = _hub?.GetModule<HudController>();
            if (stats != null)
            {
                finalDamage = attackDamage * (stats.Attack / 10f);
            }

            Vector3 attackCenter = transform.position + transform.forward * (attackRange * 0.5f);
            Collider[] hits = Physics.OverlapSphere(attackCenter, attackRange, targetLayers);

            foreach (Collider hit in hits)
            {
                var targetHealth = hit.GetComponentInParent<HealthController>();
                if (targetHealth != null)
                {
                    if (m_Health != null && targetHealth.team == m_Health.team) continue;
                    var targetDamage = hit.GetComponentInParent<DamageController>();
                    if (targetDamage != null) targetDamage.TakeDamage((int)finalDamage);
                }
            }

            if (swingVfxPrefab != null)
            {
                ProjectileController vfx = Instantiate(swingVfxPrefab, transform.position + transform.forward, transform.rotation);
                vfx.Launch(gameObject, transform.forward, 0f, m_Health != null ? m_Health.team : Team.Neutral);
            }
        }

        private bool HasParameter(Animator anim, int paramHash)
        {
            if (anim == null) return false;
            foreach (AnimatorControllerParameter param in anim.parameters)
                if (param.nameHash == paramHash) return true;
            return false;
        }
    }
}
