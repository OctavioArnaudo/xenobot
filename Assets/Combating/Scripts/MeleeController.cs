using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// Controller for Melee Combat, Contact Transmission, and Ground Slams.
    /// Handles physical collision damage transmission, visual emphasis, and multi-layer targeting.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class MeleeController : NetworkBehaviour
    {
        private const float DEFAULT_PLAYER_MELEE_DAMAGE = 2f;
        private const float DEFAULT_PLAYER_MELEE_RANGE = 3.2f;
        private const float DEFAULT_PLAYER_MELEE_COOLDOWN = 0.8f;
        private const float DEFAULT_PLAYER_SLAM_DAMAGE = 2f;
        private const float DEFAULT_PLAYER_SLAM_RADIUS = 5.5f;

        private const float DEFAULT_ENEMY_BASE_MELEE_DAMAGE = 1f;
        private const float DEFAULT_ENEMY_BASE_MELEE_RANGE = 3.8f;
        private const float DEFAULT_ENEMY_BASE_MELEE_COOLDOWN = 1.0f;
        private const float DEFAULT_ENEMY_BASE_SLAM_DAMAGE = 1f;
        private const float DEFAULT_ENEMY_BASE_SLAM_RADIUS = 4.0f;
        private const float DEFAULT_ROTATION_SPEED = 10f;

        public float AttackRange;
        public Optional<float> attackRange;
        public float AttackDamage;
        public Optional<float> attackDamage;
        public float AttackCooldown;
        public Optional<float> attackCooldown;
        public LayerMask targetLayers;

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
                    if (attackRange.use) return attackRange.value;
                    if (BalanceManager.Instance != null)
                    {
                        var stats = BalanceManager.Instance.GetEntityBalance(gameObject);
                        if (stats.meleeRange > 0) return stats.meleeRange;
                    }
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
                try
                {
                    if (attackDamage.use) return attackDamage.value;
                    if (BalanceManager.Instance != null)
                    {
                        var stats = BalanceManager.Instance.GetEntityBalance(gameObject);
                        if (stats.attackDamage > 0) return stats.attackDamage;
                    }
                }
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
                try { return slamDamage.GetValue(1f); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] slamDamage: {ex.Message}"); }

                return 1f;
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
        public float EffectiveRotationSpeed => rotationSpeedOverride.GetValue(DEFAULT_ROTATION_SPEED);

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
            m_Health = GetComponent<HealthController>() ?? GetComponentInParent<HealthController>();
            m_CharacterController = GetComponent<CharacterController>();

            if (visualsToRotate == null || visualsToRotate.Length == 0)
                visualsToRotate = GetComponentsInChildren<Renderer>();
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
            UpdateInspectorValues();
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

        // --- TRANSMISIÓN DE DAÑO POR CONTACTO FÍSICO / COLISIÓN ---

        private void OnTriggerEnter(Collider other) => TryContactDamage(other.gameObject, other.ClosestPoint(transform.position));
        private void OnTriggerStay(Collider other) => TryContactDamage(other.gameObject, other.ClosestPoint(transform.position));
        private void OnCollisionEnter(Collision collision) => TryContactDamage(collision.gameObject, collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);
        private void OnCollisionStay(Collision collision) => TryContactDamage(collision.gameObject, collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);

        private void TryContactDamage(GameObject hitObject, Vector3 impactPoint)
        {
            if (hitObject == null || hitObject == gameObject || hitObject.transform.IsChildOf(transform)) return;

            if (Time.time < m_NextAttackTime) return;

            HealthController targetHealth = hitObject.GetComponentInParent<HealthController>() ?? hitObject.GetComponent<HealthController>();

            if (targetHealth == null || targetHealth.CurrentHP <= 0) return;

            Team myTeam = m_Health != null ? m_Health.EffectiveTeam : (CompareTag("Player") ? Team.Player : Team.Enemy);
            if (targetHealth.EffectiveTeam == myTeam) return;

            // Transmitir daño melee al colisionar con cualquier parte del cuerpo/render del rival
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

        private void StartGroundSlam() {
            m_NextAttackTime = Time.time + EffectiveAttackCooldown;
            m_IsSlamming = true;

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null) {
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
            int mask = (targetLayers.value != 0) ? targetLayers.value : ~0;

            Collider[] hits = Physics.OverlapSphere(impactPosition, EffectiveSlamRadius, mask, QueryTriggerInteraction.Collide);

            foreach (Collider hit in hits)
            {
                if (hit.gameObject == gameObject || hit.transform.IsChildOf(transform)) continue;

                var targetHealth = hit.GetComponentInParent<HealthController>() ?? hit.GetComponent<HealthController>();
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
                        r.transform.rotation = Quaternion.Slerp(r.transform.rotation, targetFullRotation, EffectiveRotationSpeed * Time.deltaTime);
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
            int mask = (targetLayers.value != 0) ? targetLayers.value : ~0;

            Vector3 attackCenter = transform.position + transform.forward * (EffectiveAttackRange * 0.5f);
            Collider[] hits = Physics.OverlapSphere(attackCenter, EffectiveAttackRange, mask, QueryTriggerInteraction.Collide);

            Team myTeam = m_Health != null ? m_Health.EffectiveTeam : (CompareTag("Player") ? Team.Player : Team.Enemy);
            bool hasHitAny = false;

            foreach (Collider hit in hits)
            {
                if (hit.gameObject == gameObject || hit.transform.IsChildOf(transform)) continue;

                var targetHealth = hit.GetComponentInParent<HealthController>() ?? hit.GetComponent<HealthController>();
                if (targetHealth != null && targetHealth.EffectiveTeam != myTeam && targetHealth.CurrentHP > 0)
                {
                    targetHealth.TakeDamage((int)finalDamage);
                    hasHitAny = true;

                    Vector3 impactPos = hit.ClosestPoint(attackCenter);
                    Vector3 hitDir = (hit.transform.position - transform.position).normalized;
                    TriggerMeleeVisualEffect(impactPos, hitDir);
                }
            }

            if (!hasHitAny)
            {
                TriggerMeleeVisualEffect(attackCenter, transform.forward);
            }

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

            if (swingVfxPrefab != null)
            {
                ProjectileController vfx = Instantiate(swingVfxPrefab, position, Quaternion.LookRotation(direction));
                vfx.Launch(gameObject, direction, 0f, m_Health != null ? m_Health.EffectiveTeam : Team.Neutral);
            }
            else
            {
                GenerateHardcodedSlashVFX(position, direction);
            }
        }

        private void GenerateHardcodedSlashVFX(Vector3 position, Vector3 direction)
        {
            GameObject vfxGo = new GameObject("MeleeSlashVFX");
            vfxGo.transform.position = position;
            vfxGo.transform.rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0.01f ? direction : transform.forward);

            ParticleSystem ps = vfxGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.35f;
            main.startLifetime = 0.35f;
            main.startSpeed = 8f;
            main.startSize = 0.4f;
            main.startColor = CompareTag("Enemy") ? Color.red : new Color(1f, 0.85f, 0.1f);
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0, 25) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 45f;
            shape.radius = 0.2f;

            var renderer = vfxGo.GetComponent<ParticleSystemRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null) renderer.material = new Material(shader);

            ps.Play();
            Destroy(vfxGo, 1.5f);
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
