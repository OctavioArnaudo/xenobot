using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Melee attack system.
    /// Manages its own rotation and execution logic.
    /// Works for both Players and AI Enemies.
    /// </summary>
    public class MeleeController : NetworkBehaviour, IPlayer
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

        private HealthController m_Health;
        private PlayerController _hub;
        private float m_NextAttackTime;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        void Awake()
        {
            _hub = GetComponentInParent<PlayerController>();
            if (_hub == null) _hub = GetComponent<PlayerController>();
            if (_hub != null) Bind(_hub);

            if (visualsToRotate == null || visualsToRotate.Length == 0)
                visualsToRotate = GetComponentsInChildren<Renderer>();
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
                m_Health = _hub.GetModule<HealthController>();

                // Refresh visuals to rotate if they change with costume
                visualsToRotate = _hub.renderRoot?.GetComponentsInChildren<Renderer>() ?? GetComponentsInChildren<Renderer>();
            }
        }

        private void Update()
        {
            if (_hub != null && _hub.fire && Time.time >= m_NextAttackTime)
            {
                PerformMeleeAction();
                _hub.fire = false; // Consume input
            }
        }

        /// <summary>
        /// Main method to perform the melee action.
        /// Can optionally look at a target position.
        /// </summary>
        public void PerformMeleeAction(Vector3? targetPosition = null)
        {
            if (Time.time < m_NextAttackTime) return;

            if (targetPosition.HasValue)
            {
                RotateVisualsTowards(targetPosition.Value);
            }

            m_NextAttackTime = Time.time + attackCooldown;

            if (IsNetworkActive)
            {
                if (IsOwner) RequestMeleeServerRpc();
            }
            else
            {
                ExecuteMelee();
            }
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

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Vector3 attackCenter = transform.position + transform.forward * (attackRange * 0.5f);
            Gizmos.DrawWireSphere(attackCenter, attackRange);
        }
    }
}
