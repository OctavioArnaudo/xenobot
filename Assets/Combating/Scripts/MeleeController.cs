using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// Melee attack system.
    /// Can apply direct damage or spawn short-range effects.
    /// Works for both Players and Enemies.
    /// </summary>
    public class MeleeController : NetworkBehaviour
    {
        [Header("Settings")]
        public float attackRange = 2.5f;
        public float attackDamage = 35f;
        public float attackCooldown = 1f;
        public LayerMask targetLayers;

        [Header("Visuals (Optional)")]
        public ProjectileController swingVfxPrefab;

        private HealthController m_Health;
        private float m_NextAttackTime;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        void Awake()
        {
            m_Health = GetComponent<HealthController>();
        }

        public void OnAttack(InputValue value)
        {
            if (!value.isPressed || Time.time < m_NextAttackTime) return;
            PerformMeleeAction();
        }

        public void PerformMeleeAction()
        {
            if (Time.time < m_NextAttackTime) return;
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

        [ServerRpc]
        private void RequestMeleeServerRpc()
        {
            ExecuteMelee();
        }

        private void ExecuteMelee()
        {
            float finalDamage = attackDamage;
            if (TryGetComponent<StatsController>(out var stats))
            {
                finalDamage = attackDamage * (stats.Attack / 10f);
            }

            // 1. Physical Detection
            Vector3 attackCenter = transform.position + transform.forward * (attackRange * 0.5f);
            Collider[] hits = Physics.OverlapSphere(attackCenter, attackRange, targetLayers);

            foreach (Collider hit in hits)
            {
                var targetHealth = hit.GetComponentInParent<HealthController>();
                if (targetHealth != null)
                {
                    // Team check (Friendly fire off)
                    if (m_Health != null && targetHealth.team == m_Health.team) continue;

                    targetHealth.TakeDamage((int)finalDamage);
                }
            }

            // 2. Projectile-based Visual (as requested: "melee con proyectiles")
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
