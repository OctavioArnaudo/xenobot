using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;
using System.Collections.Generic;

namespace Combating.Scripts
{
    public enum ProjectileType
    {
        BalaFuego,      // 1. Balas de fuego (Standard/Fuego - teledirigidas, rastro de fuego)
        RayoContinuo,   // 2. Rayo continuo (Proyectil/Rayo recto continuo de energía)
        BalaCongelante, // 3. Balas congelantes (Ralentizan al rival acumulativamente con cada acierto)
        BalaCorrosiva,  // 4. Balas corrosivas (Aplican daño en el tiempo DoT acumulativo)
        BalaExplosiva   // 5. Balas explosivas (Se adhieren al rival y explotan al siguiente click/disparo)
    }

    /// <summary>
    /// Componente helper adjuntado dinámicamente a los objetivos para gestionar estados de Congelación (slow) y Corrosión (DoT).
    /// </summary>
    public class ProjectileStatusReceiver : MonoBehaviour
    {
        private int m_FreezeStacks = 0;
        private float m_FreezeTimer = 0f;
        private float m_SlowPerStack = 0.25f;

        private int m_CorrosionStacks = 0;
        private float m_CorrosionTimer = 0f;
        private float m_CorrosionDamagePerStack = 8f;
        private float m_CorrosionTickTimer = 0f;

        private HealthController m_Health;
        private EnemyController m_Enemy;

        void Awake()
        {
            m_Health = GetComponent<HealthController>() ?? GetComponentInParent<HealthController>();
            m_Enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
        }

        public void ApplyFreeze(float slowPerStack, float duration)
        {
            m_FreezeStacks++;
            m_SlowPerStack = slowPerStack;
            m_FreezeTimer = duration;
            UpdateSlowEffect();
        }

        public void ApplyCorrosion(float damagePerStack, float duration)
        {
            m_CorrosionStacks++;
            m_CorrosionDamagePerStack = damagePerStack;
            m_CorrosionTimer = duration;
        }

        void Update()
        {
            if (m_FreezeTimer > 0f)
            {
                m_FreezeTimer -= Time.deltaTime;
                if (m_FreezeTimer <= 0f)
                {
                    m_FreezeStacks = 0;
                    UpdateSlowEffect();
                }
            }

            if (m_CorrosionTimer > 0f)
            {
                m_CorrosionTimer -= Time.deltaTime;
                m_CorrosionTickTimer += Time.deltaTime;

                if (m_CorrosionTickTimer >= 0.5f)
                {
                    m_CorrosionTickTimer = 0f;
                    if (m_Health != null && m_Health.CurrentHP > 0)
                    {
                        int dotDamage = Mathf.Max(1, Mathf.RoundToInt(m_CorrosionStacks * m_CorrosionDamagePerStack * 0.5f));
                        m_Health.TakeDamage(dotDamage);
                    }
                }

                if (m_CorrosionTimer <= 0f)
                {
                    m_CorrosionStacks = 0;
                }
            }
        }

        private void UpdateSlowEffect()
        {
            float speedMultiplier = Mathf.Clamp(1.0f - (m_FreezeStacks * m_SlowPerStack), 0.1f, 1.0f);
            if (m_Enemy != null)
            {
                m_Enemy.chaseSpeed.value = speedMultiplier;
            }
        }
    }

    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class ProjectileController : NetworkBehaviour
    {
        private const ProjectileType DEFAULT_TYPE = ProjectileType.BalaFuego;
        private const float DEFAULT_SPEED = 45f;
        private const float DEFAULT_DAMAGE = 25f;
        private const float DEFAULT_LIFETIME = 3.5f;
        private const float DEFAULT_HOMING_TURN_SPEED = 30f;
        private const float DEFAULT_DETECTION_RADIUS = 40f;
        private const float DEFAULT_EXPLOSION_RADIUS = 4f;
        private const float DEFAULT_FREEZE_SLOW_AMOUNT = 0.25f;
        private const float DEFAULT_CORROSION_DAMAGE = 8f;
        private const float DEFAULT_DOT_DURATION = 3.5f;

        [Header("Tipo de Proyectil")]
        public Optional<ProjectileType> typeOverride;

        [Header("Estadísticas Generales")]
        public Optional<float> speedOverride;
        public Optional<float> damageOverride;
        public Optional<float> lifeTimeOverride;
        public Optional<float> homingTurnSpeedOverride;
        public Optional<float> detectionRadiusOverride;
        public Optional<Color> colorOverride;

        [Header("Efectos Especiales")]
        public Optional<float> explosionRadiusOverride;
        public Optional<float> freezeSlowAmountOverride;
        public Optional<float> corrosionDamageOverride;
        public Optional<float> dotDurationOverride;

        [Header("Visuales y Efectos de Impacto")]
        public GameObject visualsBalaFuego;
        public GameObject visualsRayoContinuo;
        public GameObject visualsBalaCongelante;
        public GameObject visualsBalaCorrosiva;
        public GameObject visualsBalaExplosiva;
        public ParticleSystem explosionVFX;
        public ParticleSystem impactVFX;

        private Vector3 m_Direction;
        private GameObject m_Owner;
        private Team m_OwnerTeam;
        private bool m_HasHit = false;
        private Transform m_HomingTarget;

        private bool m_IsAttached = false;
        private Transform m_AttachedTarget;
        private Vector3 m_AttachedLocalPos;

        private static List<ProjectileController> s_ActiveAttachedBombs = new List<ProjectileController>();

        public ProjectileType EffectiveType => typeOverride.GetValue(DEFAULT_TYPE);
        public float EffectiveSpeed => speedOverride.GetValue(EffectiveType == ProjectileType.RayoContinuo ? DEFAULT_SPEED * 2.2f : DEFAULT_SPEED);
        public float EffectiveDamage => damageOverride.GetValue(DEFAULT_DAMAGE);
        public float EffectiveLifeTime => lifeTimeOverride.GetValue(DEFAULT_LIFETIME);
        public float EffectiveHomingTurnSpeed => homingTurnSpeedOverride.GetValue(DEFAULT_HOMING_TURN_SPEED);
        public float EffectiveDetectionRadius => detectionRadiusOverride.GetValue(DEFAULT_DETECTION_RADIUS);
        public float EffectiveExplosionRadius => explosionRadiusOverride.GetValue(DEFAULT_EXPLOSION_RADIUS);
        public float EffectiveFreezeSlowAmount => freezeSlowAmountOverride.GetValue(DEFAULT_FREEZE_SLOW_AMOUNT);
        public float EffectiveCorrosionDamage => corrosionDamageOverride.GetValue(DEFAULT_CORROSION_DAMAGE);
        public float EffectiveDotDuration => dotDurationOverride.GetValue(DEFAULT_DOT_DURATION);
        public Color EffectiveColor => colorOverride.GetValue(GetDefaultColorForType(EffectiveType));

        private Color GetDefaultColorForType(ProjectileType type)
        {
            switch (type)
            {
                case ProjectileType.BalaFuego: return new Color(1.0f, 0.4f, 0.0f);
                case ProjectileType.RayoContinuo: return new Color(0.0f, 0.9f, 1.0f);
                case ProjectileType.BalaCongelante: return new Color(0.2f, 0.7f, 1.0f);
                case ProjectileType.BalaCorrosiva: return new Color(0.2f, 0.9f, 0.1f);
                case ProjectileType.BalaExplosiva: return new Color(1.0f, 0.85f, 0.0f);
                default: return Color.red;
            }
        }

        void Awake()
        {
            SetupPhysics();
        }

        private void Start()
        {
            if (visualsBalaFuego == null) visualsBalaFuego = transform.Find("Visuals_BalaFuego")?.gameObject;
            if (visualsRayoContinuo == null) visualsRayoContinuo = transform.Find("Visuals_RayoContinuo")?.gameObject;
            if (visualsBalaCongelante == null) visualsBalaCongelante = transform.Find("Visuals_BalaCongelante")?.gameObject;
            if (visualsBalaCorrosiva == null) visualsBalaCorrosiva = transform.Find("Visuals_BalaCorrosiva")?.gameObject;
            if (visualsBalaExplosiva == null) visualsBalaExplosiva = transform.Find("Visuals_BalaExplosiva")?.gameObject;

            RefreshVisuals();
        }

        private void SetupPhysics()
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            SphereCollider col = GetComponent<SphereCollider>();
            if (col == null) col = gameObject.AddComponent<SphereCollider>();

            if (col != null)
            {
                col.isTrigger = true;
                col.radius = 0.5f;
            }
        }

        public static void DetonateOwnerAttachedBombs(GameObject owner)
        {
            if (owner == null) return;
            var bombsToDetonate = s_ActiveAttachedBombs.FindAll(b => b != null && b.m_Owner == owner && b.m_IsAttached);
            foreach (var bomb in bombsToDetonate)
            {
                bomb.DetonateStickyBomb();
            }
        }

        public void Launch(GameObject owner, Vector3 direction, float dmg, Team team)
        {
            m_Owner = owner;
            m_Direction = direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
            m_OwnerTeam = team;

            // Detonar bombas explosivas adheridas de disparos anteriores al presionar el siguiente disparo
            DetonateOwnerAttachedBombs(owner);

            if (dmg > 0f && !damageOverride.useOverride)
            {
                damageOverride.useOverride = true;
                damageOverride.value = dmg;
            }

            if (m_Owner != null)
            {
                Collider[] ownerCols = m_Owner.GetComponentsInChildren<Collider>();
                Collider myCol = GetComponent<Collider>();
                if (myCol != null)
                {
                    foreach (var oc in ownerCols) Physics.IgnoreCollision(myCol, oc);
                }
            }

            RefreshVisuals();

            if (IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                Destroy(gameObject, EffectiveLifeTime);
        }

        private void RefreshVisuals()
        {
            ProjectileType type = EffectiveType;

            if (visualsBalaFuego != null) visualsBalaFuego.SetActive(type == ProjectileType.BalaFuego);
            if (visualsRayoContinuo != null) visualsRayoContinuo.SetActive(type == ProjectileType.RayoContinuo);
            if (visualsBalaCongelante != null) visualsBalaCongelante.SetActive(type == ProjectileType.BalaCongelante);
            if (visualsBalaCorrosiva != null) visualsBalaCorrosiva.SetActive(type == ProjectileType.BalaCorrosiva);
            if (visualsBalaExplosiva != null) visualsBalaExplosiva.SetActive(type == ProjectileType.BalaExplosiva);

            if (visualsBalaFuego == null && visualsRayoContinuo == null && visualsBalaCongelante == null &&
                visualsBalaCorrosiva == null && visualsBalaExplosiva == null)
            {
                GenerateFallbackVisuals();
            }
        }

        private void GenerateFallbackVisuals()
        {
            if (transform.Find("FallbackCore")) return;

            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "FallbackCore";
            core.transform.SetParent(transform, false);

            ProjectileType type = EffectiveType;
            if (type == ProjectileType.RayoContinuo)
                core.transform.localScale = new Vector3(0.12f, 0.12f, 1.8f);
            else if (type == ProjectileType.BalaExplosiva)
                core.transform.localScale = Vector3.one * 0.45f;
            else
                core.transform.localScale = Vector3.one * 0.35f;

            var c = core.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material mat = new Material(shader);
            Color col = EffectiveColor;
            mat.color = col;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);

            var mr = core.GetComponent<MeshRenderer>();
            mr.material = mat;

            TrailRenderer trail = core.AddComponent<TrailRenderer>();
            if (trail != null)
            {
                trail.time = (type == ProjectileType.RayoContinuo) ? 0.4f : 0.25f;
                trail.startWidth = (type == ProjectileType.RayoContinuo) ? 0.2f : 0.15f;
                trail.endWidth = 0f;
                trail.material = mat;
                trail.startColor = col;
                trail.endColor = new Color(col.r, col.g, col.b, 0f);
            }
        }

        private void Update()
        {
            if (m_IsAttached)
            {
                if (m_AttachedTarget != null)
                {
                    transform.position = m_AttachedTarget.TransformPoint(m_AttachedLocalPos);
                }
                else
                {
                    DetonateStickyBomb();
                }
                return;
            }

            GuideTowardsRivalTarget();

            float moveDistance = EffectiveSpeed * Time.deltaTime;

            if (moveDistance > 0.001f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(transform.position, 0.5f, m_Direction, moveDistance, ~0, QueryTriggerInteraction.Collide);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                foreach (var hit in hits)
                {
                    if (m_HasHit) break;
                    if (m_Owner != null && (hit.collider.gameObject == m_Owner || hit.collider.transform.IsChildOf(m_Owner.transform))) continue;

                    HealthController targetHealth = hit.collider.GetComponentInParent<HealthController>() ?? hit.collider.GetComponent<HealthController>();

                    if (hit.collider.isTrigger && targetHealth == null) continue;

                    m_HasHit = true;
                    transform.position = hit.point;

                    ProcessImpactEffects(hit.collider, targetHealth);
                    return;
                }
            }

            transform.position += m_Direction * moveDistance;

            if (m_Direction != Vector3.zero)
                transform.forward = m_Direction;
        }

        private void GuideTowardsRivalTarget()
        {
            if (m_HomingTarget == null || !m_HomingTarget.gameObject.activeInHierarchy)
            {
                m_HomingTarget = null;

                Collider[] hits = Physics.OverlapSphere(transform.position, EffectiveDetectionRadius, ~0, QueryTriggerInteraction.Collide);
                float minD = float.MaxValue;

                foreach (var h in hits)
                {
                    if (m_Owner != null && (h.gameObject == m_Owner || h.transform.IsChildOf(m_Owner.transform))) continue;

                    HealthController hc = h.GetComponentInParent<HealthController>() ?? h.GetComponent<HealthController>();
                    if (hc != null)
                    {
                        if (hc.CurrentHP <= 0) continue;

                        bool isOpposing = (m_OwnerTeam != Team.Neutral && hc.EffectiveTeam != m_OwnerTeam) ||
                                          (m_OwnerTeam == Team.Neutral && hc.EffectiveTeam != Team.Neutral) ||
                                          (m_Owner != null && m_Owner.CompareTag("Player") && (h.CompareTag("Enemy") || hc.CompareTag("Enemy"))) ||
                                          (m_Owner != null && m_Owner.CompareTag("Enemy") && (h.CompareTag("Player") || hc.CompareTag("Player")));

                        if (isOpposing)
                        {
                            float d = Vector3.Distance(transform.position, h.transform.position);
                            if (d < minD)
                            {
                                minD = d;
                                m_HomingTarget = h.transform;
                            }
                        }
                    }
                }
            }

            if (m_HomingTarget != null)
            {
                Vector3 targetCenter = m_HomingTarget.position + Vector3.up * 0.9f;
                Vector3 targetDir = (targetCenter - transform.position);
                if (targetDir.sqrMagnitude > 0.01f)
                {
                    m_Direction = Vector3.Slerp(m_Direction, targetDir.normalized, Time.deltaTime * EffectiveHomingTurnSpeed);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_HasHit || m_IsAttached) return;
            if (m_Owner != null && (other.gameObject == m_Owner || other.transform.IsChildOf(m_Owner.transform))) return;

            HealthController targetHealth = other.GetComponentInParent<HealthController>() ?? other.GetComponent<HealthController>();

            if (other.isTrigger && targetHealth == null) return;

            m_HasHit = true;
            ProcessImpactEffects(other, targetHealth);
        }

        private void ProcessImpactEffects(Collider hitCollider, HealthController targetHealth)
        {
            ProjectileType type = EffectiveType;

            switch (type)
            {
                case ProjectileType.BalaExplosiva:
                    AttachToTarget(hitCollider);
                    break;

                case ProjectileType.BalaCongelante:
                    if (targetHealth != null)
                    {
                        ApplyDirectDamage(targetHealth);
                        var status = targetHealth.GetComponent<ProjectileStatusReceiver>() ?? targetHealth.gameObject.AddComponent<ProjectileStatusReceiver>();
                        status.ApplyFreeze(EffectiveFreezeSlowAmount, EffectiveDotDuration);
                    }
                    FinalizeImpact();
                    break;

                case ProjectileType.BalaCorrosiva:
                    if (targetHealth != null)
                    {
                        ApplyDirectDamage(targetHealth);
                        var status = targetHealth.GetComponent<ProjectileStatusReceiver>() ?? targetHealth.gameObject.AddComponent<ProjectileStatusReceiver>();
                        status.ApplyCorrosion(EffectiveCorrosionDamage, EffectiveDotDuration);
                    }
                    FinalizeImpact();
                    break;

                case ProjectileType.RayoContinuo:
                case ProjectileType.BalaFuego:
                default:
                    if (targetHealth != null)
                    {
                        ApplyDirectDamage(targetHealth);
                    }
                    FinalizeImpact();
                    break;
            }
        }

        private void AttachToTarget(Collider hitCollider)
        {
            m_IsAttached = true;
            m_AttachedTarget = hitCollider.transform;
            m_AttachedLocalPos = m_AttachedTarget.InverseTransformPoint(transform.position);

            if (!s_ActiveAttachedBombs.Contains(this))
            {
                s_ActiveAttachedBombs.Add(this);
            }

            transform.SetParent(m_AttachedTarget);
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        public void DetonateStickyBomb()
        {
            if (this == null) return;

            PerformExplosion();
            FinalizeImpact();
        }

        private void ApplyDirectDamage(HealthController target)
        {
            if (target == null) return;
            if (target.gameObject == m_Owner || (m_Owner != null && target.transform.IsChildOf(m_Owner.transform))) return;

            target.TakeDamage((int)EffectiveDamage);
        }

        private void PerformExplosion()
        {
            if (explosionVFX != null) Instantiate(explosionVFX, transform.position, Quaternion.identity);

            Collider[] hits = Physics.OverlapSphere(transform.position, EffectiveExplosionRadius, ~0, QueryTriggerInteraction.Collide);
            foreach (var h in hits)
            {
                HealthController hc = h.GetComponentInParent<HealthController>() ?? h.GetComponent<HealthController>();
                if (hc != null && hc.gameObject != m_Owner)
                {
                    hc.TakeDamage((int)EffectiveDamage);
                }
            }
        }

        private void FinalizeImpact()
        {
            s_ActiveAttachedBombs.Remove(this);

            if (impactVFX != null) Instantiate(impactVFX, transform.position, Quaternion.identity);

            if (IsServer && IsSpawned) NetworkObject.Despawn();
            else Destroy(gameObject);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            s_ActiveAttachedBombs.Remove(this);
        }
    }
}
