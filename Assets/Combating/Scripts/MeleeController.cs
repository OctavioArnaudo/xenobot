using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// Controller for Melee Combat and Ground Slams.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class MeleeController : NetworkBehaviour
    {
        // --- Internal Hardcoded Melee Defaults ---
        private const float DEFAULT_PLAYER_MELEE_DAMAGE = 45f;
        private const float DEFAULT_PLAYER_MELEE_RANGE = 3.2f;
        private const float DEFAULT_PLAYER_MELEE_COOLDOWN = 0.8f;
        private const float DEFAULT_PLAYER_SLAM_DAMAGE = 65f;
        private const float DEFAULT_PLAYER_SLAM_RADIUS = 5.5f;

        private const float DEFAULT_ENEMY_BASE_MELEE_DAMAGE = 30f;
        private const float DEFAULT_ENEMY_BASE_MELEE_RANGE = 4.0f;
        private const float DEFAULT_ENEMY_BASE_MELEE_COOLDOWN = 1.0f;
        private const float DEFAULT_ENEMY_BASE_SLAM_DAMAGE = 45f;
        private const float DEFAULT_ENEMY_BASE_SLAM_RADIUS = 4.5f;

        [Header("Manual Melee Overrides")]
        public Optional<float> attackRange;
        public Optional<float> attackDamage;
        public Optional<float> attackCooldown;
        public LayerMask targetLayers;

        [Header("Manual Ground Slam Overrides")]
        public Optional<float> slamHoldDuration;
        public Optional<float> slamDamage;
        public Optional<float> slamRadius;
        public Optional<float> slamSpeed;
        public Optional<float> slamKnockbackForce;
        public ParticleSystem slamVfxPrefab;
        public AudioClip slamSound;

        [Header("Visuals")]
        public ProjectileController swingVfxPrefab;
        public Renderer[] visualsToRotate;
        public float rotationSpeed = 10f;

        private HealthController m_Health;
        private CharacterController m_CharacterController;
        private float m_NextAttackTime;
        private bool m_IsSlamming;

        private float m_HoldTimer = 0f;
        private bool m_IsHoldingClick = false;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        // --- Effective Statistics Resolvers with Optional & Fallback Protection ---

        public float EffectiveAttackRange
        {
            get
            {
                try
                {
                    var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
                    float defaultRange = (enemy != null) ? DEFAULT_ENEMY_BASE_MELEE_RANGE : DEFAULT_PLAYER_MELEE_RANGE;
                    return attackRange.GetValue(defaultRange);
                }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] attackRange: {ex.Message}"); }

                return DEFAULT_PLAYER_MELEE_RANGE;
            }
        }

        public float EffectiveAttackDamage
        {
            get
            {
                try { return attackDamage.GetValue(CalculateDynamicMeleeDamage()); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] attackDamage: {ex.Message}"); }

                return CalculateDynamicMeleeDamage();
            }
        }

        public float EffectiveAttackCooldown
        {
            get
            {
                try
                {
                    var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
                    float defaultCooldown = (enemy != null && enemy.activeArchetype == AIArchetype.CargaFrenetica) ? 0.4f : ((enemy != null) ? DEFAULT_ENEMY_BASE_MELEE_COOLDOWN : DEFAULT_PLAYER_MELEE_COOLDOWN);
                    return attackCooldown.GetValue(defaultCooldown);
                }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] attackCooldown: {ex.Message}"); }

                return DEFAULT_PLAYER_MELEE_COOLDOWN;
            }
        }

        public float EffectiveSlamDamage
        {
            get
            {
                try { return slamDamage.GetValue(EffectiveAttackDamage * 1.4f); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] slamDamage: {ex.Message}"); }

                return EffectiveAttackDamage * 1.4f;
            }
        }

        public float EffectiveSlamRadius
        {
            get
            {
                try
                {
                    var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
                    float defaultRadius = (enemy != null) ? DEFAULT_ENEMY_BASE_SLAM_RADIUS : DEFAULT_PLAYER_SLAM_RADIUS;
                    return slamRadius.GetValue(defaultRadius);
                }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] slamRadius: {ex.Message}"); }

                return DEFAULT_PLAYER_SLAM_RADIUS;
            }
        }

        public float EffectiveSlamHoldDuration => slamHoldDuration.GetValue(0.25f);
        public float EffectiveSlamSpeed => slamSpeed.GetValue(35f);
        public float EffectiveSlamKnockbackForce => slamKnockbackForce.GetValue(600f);

        private float CalculateDynamicMeleeDamage()
        {
            var pc = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
            if (pc != null || CompareTag("Player"))
            {
                return DEFAULT_PLAYER_MELEE_DAMAGE;
            }

            var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
            if (enemy != null)
            {
                float dmg = DEFAULT_ENEMY_BASE_MELEE_DAMAGE;

                switch (enemy.activeArchetype)
                {
                    case AIArchetype.CargaFrenetica: dmg *= 1.7f; break;
                    case AIArchetype.EmboscadaEnSigilo: dmg *= 1.8f; break;
                    case AIArchetype.GuardiaConEscudo: dmg *= 1.4f; break;
                    case AIArchetype.CargaDirecta: dmg *= 1.2f; break;
                    default: dmg *= 1.0f; break;
                }

                int allies = CountNearbyAllies();
                if (allies >= 3) dmg *= 0.75f;

                return dmg;
            }

            return DEFAULT_ENEMY_BASE_MELEE_DAMAGE;
        }

        private int CountNearbyAllies()
        {
            int count = 0;
            var enemies = GameObject.FindGameObjectsWithTag("Enemy");
            foreach (var e in enemies)
            {
                if (e != gameObject && Vector3.Distance(transform.position, e.transform.position) <= 25f) count++;
            }
            return count;
        }

        void Awake()
        {
            m_Health = GetComponent<HealthController>();
            m_CharacterController = GetComponent<CharacterController>();

            if (visualsToRotate == null || visualsToRotate.Length == 0)
                visualsToRotate = GetComponentsInChildren<Renderer>();
        }

        void Update()
        {
            if (IsNetworkActive && !IsOwner) return;

            if (m_IsSlamming)
            {
                HandleGroundSlamMovement();
                return;
            }

            if (Cursor.visible) return;

            HandleInput();
        }

        private void HandleInput()
        {
            if (Mouse.current == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                m_IsHoldingClick = true;
                m_HoldTimer = 0f;
            }

            if (Mouse.current.leftButton.isPressed && m_IsHoldingClick)
            {
                m_HoldTimer += Time.deltaTime;
                bool isAirborne = m_CharacterController != null && !m_CharacterController.isGrounded;

                if (isAirborne && m_HoldTimer >= EffectiveSlamHoldDuration && Time.time >= m_NextAttackTime)
                {
                    m_IsHoldingClick = false;
                    StartGroundSlam();
                }
            }

            if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                if (m_IsHoldingClick)
                {
                    m_IsHoldingClick = false;
                    if (Time.time >= m_NextAttackTime)
                    {
                        PerformMeleeAction();
                    }
                }
            }
        }

        private void StartGroundSlam()
        {
            m_NextAttackTime = Time.time + EffectiveAttackCooldown;
            m_IsSlamming = true;

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && HasParameter(anim, "groundSlamStart"))
            {
                anim.SetTrigger("groundSlamStart");
            }
        }

        private void HandleGroundSlamMovement()
        {
            if (m_CharacterController != null)
            {
                m_CharacterController.Move(Vector3.down * (EffectiveSlamSpeed * Time.deltaTime));

                if (m_CharacterController.isGrounded)
                {
                    m_IsSlamming = false;
                    TriggerSlamImpact();
                }
            }
            else
            {
                m_IsSlamming = false;
            }
        }

        public void PerformMeleeAction(Vector3? targetPosition = null)
        {
            if (targetPosition.HasValue)
            {
                RotateVisualsTowards(targetPosition.Value);
            }

            m_NextAttackTime = Time.time + EffectiveAttackCooldown;

            if (IsNetworkActive)
            {
                if (IsOwner) RequestMeleeServerRpc();
            }
            else
            {
                ExecuteMelee();
            }
        }

        private void TriggerSlamImpact()
        {
            if (IsNetworkActive)
            {
                if (IsOwner) RequestSlamServerRpc(transform.position);
            }
            else
            {
                ExecuteGroundSlam(transform.position);
            }
        }

        [ServerRpc]
        private void RequestSlamServerRpc(Vector3 impactPosition)
        {
            ExecuteGroundSlam(impactPosition);
        }

        private void ExecuteGroundSlam(Vector3 impactPosition)
        {
            float finalDamage = EffectiveSlamDamage;

            Collider[] hits = Physics.OverlapSphere(impactPosition, EffectiveSlamRadius, targetLayers);

            foreach (Collider hit in hits)
            {
                if (hit.gameObject == gameObject) continue;

                var targetHealth = hit.GetComponentInParent<HealthController>();
                if (targetHealth != null)
                {
                    if (m_Health != null && targetHealth.EffectiveTeam == m_Health.EffectiveTeam) continue;
                    targetHealth.TakeDamage((int)finalDamage);
                }

                Rigidbody rb = hit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.AddExplosionForce(EffectiveSlamKnockbackForce, impactPosition, EffectiveSlamRadius, 0.5f, ForceMode.Impulse);
                }
            }

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && HasParameter(anim, "groundSlamImpact"))
            {
                anim.SetTrigger("groundSlamImpact");
            }

            if (slamVfxPrefab != null)
            {
                Instantiate(slamVfxPrefab, impactPosition, Quaternion.identity);
            }

            if (slamSound != null)
            {
                AudioSource.PlayClipAtPoint(slamSound, impactPosition);
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
            float finalDamage = EffectiveAttackDamage;

            Vector3 attackCenter = transform.position + transform.forward * (EffectiveAttackRange * 0.5f);
            Collider[] hits = Physics.OverlapSphere(attackCenter, EffectiveAttackRange, targetLayers);

            foreach (Collider hit in hits)
            {
                var targetHealth = hit.GetComponentInParent<HealthController>();
                if (targetHealth != null)
                {
                    if (m_Health != null && targetHealth.EffectiveTeam == m_Health.EffectiveTeam) continue;
                    targetHealth.TakeDamage((int)finalDamage);
                }
            }

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && HasParameter(anim, "meleeAttack"))
            {
                anim.SetTrigger("meleeAttack");
            }

            if (swingVfxPrefab != null)
            {
                ProjectileController vfx = Instantiate(swingVfxPrefab, transform.position + transform.forward, transform.rotation);
                vfx.Launch(gameObject, transform.forward, 0f, m_Health != null ? m_Health.EffectiveTeam : Team.Neutral);
            }
        }

        private bool HasParameter(Animator animator, string paramName)
        {
            if (animator == null) return false;
            foreach (AnimatorControllerParameter param in animator.parameters)
                if (param.name == paramName) return true;
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Vector3 attackCenter = transform.position + transform.forward * (EffectiveAttackRange * 0.5f);
            Gizmos.DrawWireSphere(attackCenter, EffectiveAttackRange);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, EffectiveSlamRadius);
        }
    }
}
