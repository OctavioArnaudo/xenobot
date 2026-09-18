using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// Melee attack system with Ground Slam functionality.
    /// Manages its own rotation, melee attacks, and air slams.
    /// Works for both Players and AI Enemies.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class MeleeController : NetworkBehaviour
    {
        [Header("Normal Melee Settings")]
        public float attackRange = 2.5f;
        public float attackDamage = 35f;
        public float attackCooldown = 1f;
        public LayerMask targetLayers;

        [Header("Ground Slam Settings")]
        public float slamDamage = 50f;
        public float slamRadius = 5f;
        public float slamSpeed = 35f;
        public float slamKnockbackForce = 600f;
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

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        void Awake()
        {
            m_Health = GetComponent<HealthController>();
            m_CharacterController = GetComponent<CharacterController>();

            if (visualsToRotate == null || visualsToRotate.Length == 0)
                visualsToRotate = GetComponentsInChildren<Renderer>();
        }

        void Update()
        {
            // Solo el cliente dueño del personaje lee el input local
            if (IsNetworkActive && !IsOwner) return;

            // Procesar descenso rápido si el personaje está en medio de un Ground Slam
            if (m_IsSlamming)
            {
                HandleGroundSlamMovement();
                return;
            }

            // Bloquear el ataque si el inventario o menú están abiertos
            if (Cursor.visible) return;

            // Detección directa del Clic Izquierdo del ratón
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                PerformMeleeAction();
            }
        }

        private void HandleGroundSlamMovement()
        {
            if (m_CharacterController != null)
            {
                // Forzar caída rápida en picado
                m_CharacterController.Move(Vector3.down * (slamSpeed * Time.deltaTime));

                // Detectar colisión con el suelo para detonar el impacto
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

        public void OnAttack(InputValue value)
        {
            if (!value.isPressed || Time.time < m_NextAttackTime || m_IsSlamming) return;
            if (Cursor.visible) return;

            PerformMeleeAction();
        }

        /// <summary>
        /// Main method to perform melee or slam action based on grounded state.
        /// </summary>
        public void PerformMeleeAction(Vector3? targetPosition = null)
        {
            if (Time.time < m_NextAttackTime || m_IsSlamming) return;

            // Detección de estado en el aire
            bool isAirborne = m_CharacterController != null && !m_CharacterController.isGrounded;

            if (isAirborne)
            {
                // Iniciar Ground Slam
                m_NextAttackTime = Time.time + attackCooldown;
                m_IsSlamming = true;

                Animator anim = GetComponentInChildren<Animator>();
                if (anim != null && HasParameter(anim, "groundSlamStart"))
                {
                    anim.SetTrigger("groundSlamStart");
                }
                return;
            }

            // Ataque Melee normal (Suelo)
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
            float finalDamage = slamDamage;

            // Integración con StatsController
            var stats = GetComponent<StatsController>() ?? GetComponentInParent<StatsController>();
            if (stats != null)
            {
                finalDamage = slamDamage * (stats.Attack / 10f);
            }

            // 1. Detección física en área (AOE)
            Collider[] hits = Physics.OverlapSphere(impactPosition, slamRadius, targetLayers);

            foreach (Collider hit in hits)
            {
                if (hit.gameObject == gameObject) continue;

                var targetHealth = hit.GetComponentInParent<HealthController>();
                if (targetHealth != null)
                {
                    if (m_Health != null && targetHealth.team == m_Health.team) continue;
                    targetHealth.TakeDamage((int)finalDamage);
                }

                // Empuje físico con Rigidbody
                Rigidbody rb = hit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.AddExplosionForce(slamKnockbackForce, impactPosition, slamRadius, 0.5f, ForceMode.Impulse);
                }
            }

            // 2. Animación de impacto
            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && HasParameter(anim, "groundSlamImpact"))
            {
                anim.SetTrigger("groundSlamImpact");
            }

            // 3. Efecto visual EFX y Sonido
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
            float finalDamage = attackDamage;

            var stats = GetComponent<StatsController>() ?? GetComponentInParent<StatsController>();
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
                vfx.Launch(gameObject, transform.forward, 0f, m_Health != null ? m_Health.team : Team.Neutral);
            }
        }

        private bool HasParameter(Animator animator, string paramName)
        {
            foreach (AnimatorControllerParameter param in animator.parameters)
                if (param.name == paramName) return true;
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            // Gizmo del ataque cuerpo a cuerpo normal (Rojo)
            Gizmos.color = Color.red;
            Vector3 attackCenter = transform.position + transform.forward * (attackRange * 0.5f);
            Gizmos.DrawWireSphere(attackCenter, attackRange);

            // Gizmo del radio del Ground Slam (Amarillo)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, slamRadius);
        }
    }
}