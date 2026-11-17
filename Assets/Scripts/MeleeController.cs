using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Effective;
using Runtime.Scripts.Projectile;
using Runtime.Scripts.Health;
using Runtime.Scripts.Player;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Enemy;
using Runtime.Scripts.Hud;

namespace Runtime.Scripts.Melee
{
    /// <summary>
    /// Controller for Melee Combat, Contact Transmission, and Ground Slams.
    /// Handles physical collision damage transmission, visual emphasis, and multi-layer targeting.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class MeleeController : NetworkBehaviour
    {
        private const float DEFAULT_PLAYER_MELEE_DAMAGE = 2f;
        private const float DEFAULT_PLAYER_MELEE_RANGE = 5.5f;
        private const float DEFAULT_PLAYER_MELEE_COOLDOWN = 0.8f;
        private const float DEFAULT_PLAYER_SLAM_DAMAGE = 2f;
        private const float DEFAULT_PLAYER_SLAM_RADIUS = 5.5f;

        private const float DEFAULT_ENEMY_BASE_MELEE_DAMAGE = 1f;
        private const float DEFAULT_ENEMY_BASE_MELEE_RANGE = 3.8f;
        private const float DEFAULT_ENEMY_BASE_MELEE_COOLDOWN = 1.0f;
        private const float DEFAULT_ENEMY_BASE_SLAM_DAMAGE = 1f;
        private const float DEFAULT_ENEMY_BASE_SLAM_RADIUS = 4.0f;
        private const float DEFAULT_ROTATION_SPEED = 10f;
        private const float DEFAULT_SLAM_HOLD_DURATION = 0.15f;
        private const float DEFAULT_SLAM_SPEED = 35f;
        private const float DEFAULT_SLAM_KNOCKBACK_FORCE = 600f;

        public float AttackRange;
        public Optional<float> attackRange;
        public float AttackDamage;
        public Optional<float> attackDamage;
        public float AttackCooldown;
        public Optional<float> attackCooldown;
        public LayerMask targetLayers;

        [Header("Specific Hitboxes & MeshColliders")]
        public MeshCollider[] specificMeshColliders;
        public Collider[] meleeHitboxes;

        [Header("Ground Slam")]
        public float SlamHoldDuration;
        public Optional<float> slamHoldDuration;
        public float SlamDamage;
        public Optional<float> slamDamage;
        public float SlamRadius;
        public Optional<float> slamRadius;
        public float SlamSpeed;
        public Optional<float> slamSpeed;
        public float SlamKnockbackForce;
        public Optional<float> slamKnockbackForce;
        public ParticleSystem slamVfxPrefab;
        public AudioClip slamSound;

        [Header("Visuals & Audio Emphasis")]
        public float RotationSpeedOverride;
        public Optional<float> rotationSpeedOverride;
        public ProjectileController swingVfxPrefab;
        public Renderer[] visualsToRotate;
        public AudioClip meleeHitSound;

        private HealthController m_Health;
        private CharacterController m_CharacterController;
        private PlayerController m_Player;
        private float m_NextAttackTime;
        private bool m_IsSlamming;

        private float m_HoldTimer = 0f;
        private bool m_IsHoldingClick = false;
        private int m_LastExternalTriggerFrame = -1;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        public bool IsSlamming => m_IsSlamming;

        // --- Effective Statistics Resolvers with Optional & Fallback Protection ---

        public float EffectiveAttackRange
        {
            get
            {
                var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
                float defaultRange = (enemy != null) ? DEFAULT_ENEMY_BASE_MELEE_RANGE : DEFAULT_PLAYER_MELEE_RANGE;
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return attackRange.Get(AttackRange, defaultRange, stats.meleeRange);
            }
        }

        public float EffectiveAttackDamage
        {
            get
            {
                float defaultDamage = CalculateDynamicMeleeDamage();
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return attackDamage.Get(AttackDamage, defaultDamage, stats.attackDamage);
            }
        }

        public float EffectiveAttackCooldown
        {
            get
            {
                var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
                float defaultCooldown = (enemy != null && enemy.activeArchetype == AIArchetype.CargaFrenetica) ? 0.4f : ((enemy != null) ? DEFAULT_ENEMY_BASE_MELEE_COOLDOWN : DEFAULT_PLAYER_MELEE_COOLDOWN);
                return attackCooldown.Get(AttackCooldown, defaultCooldown);
            }
        }

        public float EffectiveSlamDamage => slamDamage.Get(SlamDamage, DEFAULT_PLAYER_SLAM_DAMAGE);

        public float EffectiveSlamRadius
        {
            get
            {
                var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
                float defaultRadius = (enemy != null) ? DEFAULT_ENEMY_BASE_SLAM_RADIUS : DEFAULT_PLAYER_SLAM_RADIUS;
                return slamRadius.Get(SlamRadius, defaultRadius);
            }
        }

        public float EffectiveSlamHoldDuration => slamHoldDuration.Get(SlamHoldDuration, DEFAULT_SLAM_HOLD_DURATION);
        public float EffectiveSlamSpeed => slamSpeed.Get(SlamSpeed, DEFAULT_SLAM_SPEED);
        public float EffectiveSlamKnockbackForce => slamKnockbackForce.Get(SlamKnockbackForce, DEFAULT_SLAM_KNOCKBACK_FORCE);
        public float EffectiveRotationSpeed => rotationSpeedOverride.Get(RotationSpeedOverride, DEFAULT_ROTATION_SPEED);

        private float CalculateDynamicMeleeDamage()
        {
            var pc = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
            if (pc != null || CompareTag("Player"))
            {
                var hud = GetComponent<HudController>() ?? GetComponentInParent<HudController>() ?? HudController.Instance;
                int lvl = (hud != null) ? Mathf.Max(1, hud.Level) : 1;
                return DEFAULT_PLAYER_MELEE_DAMAGE + (lvl - 1);
            }

            return DEFAULT_ENEMY_BASE_MELEE_DAMAGE;
        }

        void Awake()
        {
            m_Health = GetComponent<HealthController>() ?? GetComponentInParent<HealthController>();
            m_CharacterController = GetComponent<CharacterController>();
            m_Player = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();

            if (visualsToRotate == null || visualsToRotate.Length == 0)
                visualsToRotate = GetComponentsInChildren<Renderer>();

            // Rigidbody cinemático en el objeto raíz para habilitar callbacks de físicas en MeshColliders
            Rigidbody rb = GetComponent<Rigidbody>() ?? GetComponentInParent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            SetupSpecificMeshColliders();
        }

        public void SetupSpecificMeshColliders()
        {
            if (specificMeshColliders == null || specificMeshColliders.Length == 0)
            {
                specificMeshColliders = GetComponentsInChildren<MeshCollider>(true);
            }

            if (specificMeshColliders != null)
            {
                foreach (var meshCol in specificMeshColliders)
                {
                    if (meshCol == null || meshCol.gameObject == gameObject) continue;

                    if (!meshCol.convex)
                    {
                        meshCol.convex = true;
                    }

                    if (!meshCol.gameObject.TryGetComponent<MeleeHitboxBridge>(out var bridge))
                    {
                        bridge = meshCol.gameObject.AddComponent<MeleeHitboxBridge>();
                    }
                    bridge.ownerMeleeController = this;
                }
            }

            if (meleeHitboxes == null || meleeHitboxes.Length == 0)
            {
                meleeHitboxes = GetComponentsInChildren<Collider>(true)
                    .Where(c => !(c is CharacterController) && c.gameObject != gameObject)
                    .ToArray();
            }

            if (meleeHitboxes != null)
            {
                foreach (var col in meleeHitboxes)
                {
                    if (col == null || col.gameObject == gameObject) continue;

                    if (!col.gameObject.TryGetComponent<MeleeHitboxBridge>(out var bridge))
                    {
                        bridge = col.gameObject.AddComponent<MeleeHitboxBridge>();
                    }
                    bridge.ownerMeleeController = this;
                }
            }
        }

        private void UpdateInspectorValues()
        {
            AttackRange = EffectiveAttackRange;
            AttackDamage = EffectiveAttackDamage;
            AttackCooldown = EffectiveAttackCooldown;
            SlamHoldDuration = EffectiveSlamHoldDuration;
            SlamDamage = EffectiveSlamDamage;
            SlamRadius = EffectiveSlamRadius;
            SlamSpeed = EffectiveSlamSpeed;
            SlamKnockbackForce = EffectiveSlamKnockbackForce;
            RotationSpeedOverride = EffectiveRotationSpeed;
        }

        private void OnValidate()
        {
            UpdateInspectorValues();
        }

        void Update()
        {
#if UNITY_EDITOR
            UpdateInspectorValues();
#endif
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

            if (m_Player == null) m_Player = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();

            // Inicio de la pulsación
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                m_IsHoldingClick = true;
                m_HoldTimer = 0f;
            }

            // Mantenimiento sostenido del click para Ground Slam en el aire
            if (Mouse.current.leftButton.isPressed && m_IsHoldingClick)
            {
                m_HoldTimer += Time.deltaTime;

                bool isAirborne = (m_Player != null) ? !m_Player.Grounded : (m_CharacterController != null && !m_CharacterController.isGrounded);

                if (isAirborne && m_HoldTimer >= EffectiveSlamHoldDuration)
                {
                    m_IsHoldingClick = false;
                    StartGroundSlam();
                    return;
                }
            }

            // Al soltar el click
            if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                if (m_IsHoldingClick)
                {
                    m_IsHoldingClick = false;

                    // Solo ejecuta golpe al soltar si no se activó ya en este ciclo ni estamos en Slam
                    if (!m_IsSlamming && Time.time >= m_NextAttackTime && m_LastExternalTriggerFrame != Time.frameCount)
                    {
                        PerformMeleeAction();
                    }
                }
            }
        }

        // --- TRANSMISIÓN DE DAÑO POR CONTACTO FÍSICO / COLISIÓN ---

        private static Vector3 GetSafeImpactPoint(Collider col, Vector3 referencePosition)
        {
            if (col == null) return referencePosition;
            if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider || (col is MeshCollider mc && mc.convex))
            {
                return col.ClosestPoint(referencePosition);
            }
            return col.bounds.ClosestPoint(referencePosition);
        }

        private void OnTriggerEnter(Collider other) => TryContactDamage(other.gameObject, GetSafeImpactPoint(other, transform.position));
        private void OnTriggerStay(Collider other) => TryContactDamage(other.gameObject, GetSafeImpactPoint(other, transform.position));
        private void OnCollisionEnter(Collision collision) => TryContactDamage(collision.gameObject, collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);
        private void OnCollisionStay(Collision collision) => TryContactDamage(collision.gameObject, collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit != null && hit.gameObject != null)
            {
                TryContactDamage(hit.gameObject, hit.point);
            }
        }

        private void TryContactDamage(GameObject hitObject, Vector3 impactPoint)
        {
            if (m_Player != null) return;   // el player NO golpea por contacto: solo con clic (PerformMeleeAction). El dano por contacto queda para enemigos.
            if (hitObject == null || hitObject == gameObject || hitObject.transform.IsChildOf(transform)) return;

            if (Time.time < m_NextAttackTime) return;

            HealthController targetHealth = hitObject.GetComponentInParent<HealthController>()
                                         ?? hitObject.GetComponent<HealthController>()
                                         ?? hitObject.GetComponentInChildren<HealthController>()
                                         ?? hitObject.transform.root.GetComponentInChildren<HealthController>(true);

            if (targetHealth == null || targetHealth.EffectiveCurrentHealth <= 0) return;

            Team myTeam = m_Health != null ? m_Health.EffectiveTeam : (CompareTag("Player") || transform.root.CompareTag("Player") ? Team.Player : Team.Enemy);
            Team targetTeam = targetHealth.EffectiveTeam;

            bool isHostile = (myTeam != targetTeam) ||
                             (myTeam == Team.Enemy && (hitObject.CompareTag("Player") || hitObject.transform.root.CompareTag("Player") || hitObject.GetComponentInParent<PlayerController>() != null || hitObject.GetComponentInChildren<PlayerController>() != null)) ||
                             (myTeam == Team.Player && (hitObject.CompareTag("Enemy") || hitObject.transform.root.CompareTag("Enemy") || hitObject.GetComponentInParent<EnemyController>() != null || hitObject.GetComponentInChildren<EnemyController>() != null)) ||
                             (myTeam == Team.Neutral && targetTeam != Team.Neutral);

            if (!isHostile) return;

            float damage = EffectiveAttackDamage;
            targetHealth.TakeDamage((int)damage);

            m_NextAttackTime = Time.time + EffectiveAttackCooldown;

            Vector3 hitDirection = (hitObject.transform.position - transform.position).normalized;
            TriggerMeleeVisualEffect(impactPoint, hitDirection);

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && HasParameter(anim, "meleeAttack"))
            {
                anim.SetTrigger("meleeAttack");
            }
        }

        private void StartGroundSlam()
        {
            m_NextAttackTime = Time.time + EffectiveAttackCooldown;
            m_IsSlamming = true;

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null)
            {
                if (HasParameter(anim, "smashDown"))
                    anim.SetTrigger("smashDown");
                else if (HasParameter(anim, "groundSlamStart"))
                    anim.SetTrigger("groundSlamStart");
            }
        }

        private void HandleGroundSlamMovement()
        {
            if (m_CharacterController != null)
            {
                m_CharacterController.Move(Vector3.down * (EffectiveSlamSpeed * Time.deltaTime));

                bool isGrounded = m_CharacterController.isGrounded || (m_Player != null && m_Player.Grounded);

                if (isGrounded)
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
            if (Time.time < m_NextAttackTime || m_IsSlamming) return;

            // Si está en el aire y acaba de pulsar clic, damos margen al Ground Slam si mantiene presionado
            if (m_Player != null && !m_Player.Grounded && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            m_LastExternalTriggerFrame = Time.frameCount;

            if (targetPosition.HasValue)
            {
                RotateVisualsTowards(targetPosition.Value);
            }
            else if (m_Player != null && Camera.main != null)
            {
                // Alinear al jugador con el frente horizontal de la cámara al golpear en melee
                Vector3 camForward = Camera.main.transform.forward;
                camForward.y = 0f;
                if (camForward.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(camForward.normalized);
                }
            }

            m_NextAttackTime = Time.time + EffectiveAttackCooldown;

            if (IsNetworkActive)
            {
                if (IsServer) ExecuteMelee();
                else if (IsOwner) RequestMeleeServerRpc();
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
            ExecuteGroundSlamDamageServer(impactPosition);
            NotifySlamClientRpc(impactPosition);
        }

        [ClientRpc]
        private void NotifySlamClientRpc(Vector3 impactPosition)
        {
            PlaySlamEffects(impactPosition);
        }

        private void PlaySlamEffects(Vector3 impactPosition)
        {
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

        private void ExecuteGroundSlamDamageServer(Vector3 impactPosition)
        {
            float finalDamage = EffectiveSlamDamage;
            int mask = ~0;   // el filtro real es HealthController + equipo; asi ningun enemigo queda fuera por su layer

            Collider[] hits = Physics.OverlapSphere(impactPosition, EffectiveSlamRadius, mask, QueryTriggerInteraction.Collide);
            if (IsPlayerSide) HitBreakables(impactPosition, EffectiveSlamRadius, finalDamage);

            HashSet<GameObject> processed = new HashSet<GameObject>();
            foreach (Collider hit in hits)
            {
                if (hit.gameObject == gameObject || hit.transform.IsChildOf(transform)) continue;

                var targetHealth = hit.GetComponentInParent<HealthController>()
                                ?? hit.GetComponent<HealthController>()
                                ?? hit.transform.root.GetComponentInChildren<HealthController>(true);

                if (targetHealth != null && !processed.Contains(targetHealth.gameObject))
                {
                    if (m_Health != null && targetHealth.EffectiveTeam == m_Health.EffectiveTeam) continue;
                    processed.Add(targetHealth.gameObject);
                    targetHealth.TakeDamage((int)finalDamage);
                }

                Rigidbody rb = hit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.AddExplosionForce(EffectiveSlamKnockbackForce, impactPosition, EffectiveSlamRadius, 0.5f, ForceMode.Impulse);
                }
            }
        }

        private void ExecuteGroundSlam(Vector3 impactPosition)
        {
            ExecuteGroundSlamDamageServer(impactPosition);
            PlaySlamEffects(impactPosition);
        }

        private bool IsPlayerSide => GetComponentInParent<EnemyController>() == null;

        // Cristales y arboles (Smashable): ignora layers y triggers.
        private static void HitBreakables(Vector3 center, float radius, float damage)
        {
            var seen = new HashSet<Smashable>();
            foreach (var c in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var b = c.GetComponentInParent<Smashable>();
                if (b != null && seen.Add(b)) b.Damage(damage);
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
                        r.transform.rotation = Quaternion.Slerp(r.transform.rotation, targetFullRotation, EffectiveRotationSpeed * Time.deltaTime);
                }
            }
        }

        [ServerRpc]
        private void RequestMeleeServerRpc()
        {
            ExecuteMeleeDamageServer();
            Vector3 attackCenter = transform.position + Vector3.up * 1.0f + transform.forward * (EffectiveAttackRange * 0.5f);
            NotifyMeleeClientRpc(attackCenter, transform.forward);
        }

        [ClientRpc]
        private void NotifyMeleeClientRpc(Vector3 attackCenter, Vector3 forwardDir)
        {
            TriggerMeleeVisualEffect(attackCenter, forwardDir);

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && HasParameter(anim, "meleeAttack"))
            {
                anim.SetTrigger("meleeAttack");
            }
        }

        public void TryContactDamageFromHitbox(GameObject hitObject, Vector3 impactPoint)
        {
            TryContactDamage(hitObject, impactPoint);
        }

        private void ExecuteMeleeDamageServer()
        {
            float finalDamage = EffectiveAttackDamage;
            // Usamos ~0 para no dejar fuera a enemigos por configuración de LayerMask; ProcessHitTarget filtra por HealthController y Team
            int mask = ~0;

            HashSet<GameObject> hitTargets = new HashSet<GameObject>();

            // 1. Detección por colisionadores/MeshColliders específicos asignados o detectados en la jerarquía
            List<Collider> allHitboxes = new List<Collider>();
            if (specificMeshColliders != null) allHitboxes.AddRange(specificMeshColliders.Where(c => c != null));
            if (meleeHitboxes != null) allHitboxes.AddRange(meleeHitboxes.Where(c => c != null));

            foreach (var col in allHitboxes)
            {
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;

                Bounds bounds = col.bounds;
                float radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z, 0.85f);
                Collider[] hitsFromMesh = Physics.OverlapSphere(bounds.center, radius, mask, QueryTriggerInteraction.Collide);

                foreach (var hit in hitsFromMesh)
                {
                    ProcessHitTarget(hit, finalDamage, hitTargets);
                }
            }

            // 2. Detección frontal amplia por esfera de respaldo
            Vector3 attackCenter = transform.position + Vector3.up * 1.0f + transform.forward * (EffectiveAttackRange * 0.5f);
            Collider[] hitsFrontal = Physics.OverlapSphere(attackCenter, EffectiveAttackRange, mask, QueryTriggerInteraction.Collide);

            foreach (var hit in hitsFrontal)
            {
                ProcessHitTarget(hit, finalDamage, hitTargets);
            }

            if (IsPlayerSide) HitBreakables(attackCenter, EffectiveAttackRange, finalDamage);
        }

        private void ProcessHitTarget(Collider hit, float finalDamage, HashSet<GameObject> processedTargets)
        {
            if (hit == null || hit.gameObject == gameObject || hit.transform.IsChildOf(transform)) return;

            var targetHealth = hit.GetComponentInParent<HealthController>()
                            ?? hit.GetComponent<HealthController>()
                            ?? hit.GetComponentInChildren<HealthController>()
                            ?? hit.transform.root.GetComponentInChildren<HealthController>(true);

            if (targetHealth == null || targetHealth.EffectiveCurrentHealth <= 0) return;

            if (processedTargets.Contains(targetHealth.gameObject)) return;

            Team myTeam = m_Health != null ? m_Health.EffectiveTeam : (CompareTag("Player") || transform.root.CompareTag("Player") ? Team.Player : Team.Enemy);
            Team targetTeam = targetHealth.EffectiveTeam;

            bool isHostile = (myTeam != targetTeam) ||
                             (myTeam == Team.Enemy && (hit.CompareTag("Player") || hit.transform.root.CompareTag("Player") || hit.GetComponentInParent<PlayerController>() != null || hit.GetComponentInChildren<PlayerController>() != null)) ||
                             (myTeam == Team.Player && (hit.CompareTag("Enemy") || hit.transform.root.CompareTag("Enemy") || hit.GetComponentInParent<EnemyController>() != null || hit.GetComponentInChildren<EnemyController>() != null)) ||
                             (myTeam == Team.Neutral && targetTeam != Team.Neutral);

            if (isHostile)
            {
                processedTargets.Add(targetHealth.gameObject);
                targetHealth.TakeDamage((int)finalDamage);
            }
        }

        private void ExecuteMelee()
        {
            ExecuteMeleeDamageServer();
            Vector3 attackCenter = transform.position + Vector3.up * 1.0f + transform.forward * (EffectiveAttackRange * 0.5f);
            TriggerMeleeVisualEffect(attackCenter, transform.forward);

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && HasParameter(anim, "meleeAttack"))
            {
                anim.SetTrigger("meleeAttack");
            }
        }

        private void TriggerMeleeVisualEffect(Vector3 position, Vector3 direction)
        {
            if (meleeHitSound != null)
            {
                AudioSource.PlayClipAtPoint(meleeHitSound, position);
            }

            CombatFx.Slash(position, direction, EffectiveAttackRange * .9f, CompareTag("Enemy") ? new Color(1f, .25f, .2f, .9f) : new Color(.7f, .95f, 1f, .9f));
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
            Vector3 attackCenter = transform.position + Vector3.up * 1.0f + transform.forward * (EffectiveAttackRange * 0.5f);
            Gizmos.DrawWireSphere(attackCenter, EffectiveAttackRange);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, EffectiveSlamRadius);
        }
    }

    /// <summary>
    /// Componente puente adjuntado automáticamente a GameObjects hijos con MeshColliders / Colliders específicos.
    /// Redirige los eventos de colisión y triggers de Unity al MeleeController principal.
    /// </summary>
    public class MeleeHitboxBridge : MonoBehaviour
    {
        public MeleeController ownerMeleeController;

        private Vector3 GetSafePoint(Collider col)
        {
            if (col == null) return transform.position;
            if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider || (col is MeshCollider mc && mc.convex))
            {
                return col.ClosestPoint(transform.position);
            }
            return col.bounds.ClosestPoint(transform.position);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (ownerMeleeController != null && other != null)
                ownerMeleeController.TryContactDamageFromHitbox(other.gameObject, GetSafePoint(other));
        }

        private void OnTriggerStay(Collider other)
        {
            if (ownerMeleeController != null && other != null)
                ownerMeleeController.TryContactDamageFromHitbox(other.gameObject, GetSafePoint(other));
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (ownerMeleeController != null && collision != null && collision.gameObject != null)
                ownerMeleeController.TryContactDamageFromHitbox(collision.gameObject, collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (ownerMeleeController != null && collision != null && collision.gameObject != null)
                ownerMeleeController.TryContactDamageFromHitbox(collision.gameObject, collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (ownerMeleeController != null && hit != null && hit.gameObject != null)
                ownerMeleeController.TryContactDamageFromHitbox(hit.gameObject, hit.point);
        }
    }
}