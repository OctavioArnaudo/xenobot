using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Combating.Scripts
{
    [RequireComponent(typeof(CharacterController))]
    public class MeleeController : NetworkBehaviour
    {
        [Header("Normal Melee Settings")]
        public float attackRange = 2.5f;
        public float attackDamage = 35f;
        public float attackCooldown = 1f;
        public LayerMask targetLayers;

        [Header("Ground Slam Settings")]
        public float slamHoldDuration = 0.25f; // Tiempo en segundos manteniendo el clic (250 ms)
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

        // Control de tiempo para el ataque cargado
        private float m_HoldTimer = 0f;
        private bool m_IsHoldingClick = false;

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

            // 1. Al presionar el clic, iniciamos el conteo
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                m_IsHoldingClick = true;
                m_HoldTimer = 0f;
            }

            // 2. Mientras se mantenga presionado el clic
            if (Mouse.current.leftButton.isPressed && m_IsHoldingClick)
            {
                m_HoldTimer += Time.deltaTime;

                bool isAirborne = m_CharacterController != null && !m_CharacterController.isGrounded;

                // Si está en el aire y mantuvo presionado el tiempo necesario -> Detonar Ground Slam
                if (isAirborne && m_HoldTimer >= slamHoldDuration && Time.time >= m_NextAttackTime)
                {
                    m_IsHoldingClick = false;
                    StartGroundSlam();
                }
            }

            // 3. Al soltar el clic
            if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                // Si soltó rápido (clic corto) o estaba en el suelo -> Ataque Melee normal
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
            m_NextAttackTime = Time.time + attackCooldown;
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
                m_CharacterController.Move(Vector3.down * (slamSpeed * Time.deltaTime));

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

            var stats = GetComponent<HudController>() ?? GetComponentInParent<HudController>();
            if (stats != null)
            {
                finalDamage = slamDamage * (stats.Attack / 10f);
            }

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

                Rigidbody rb = hit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.AddExplosionForce(slamKnockbackForce, impactPosition, slamRadius, 0.5f, ForceMode.Impulse);
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
            float finalDamage = attackDamage;

            var stats = GetComponent<HudController>() ?? GetComponentInParent<HudController>();
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
            Gizmos.color = Color.red;
            Vector3 attackCenter = transform.position + transform.forward * (attackRange * 0.5f);
            Gizmos.DrawWireSphere(attackCenter, attackRange);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, slamRadius);
        }
    }
}