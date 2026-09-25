using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public enum ProjectileType { Standard, Laser, Explosive, Homing, Random }

    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class ProjectileController : NetworkBehaviour
    {
        // --- Internal Hardcoded Projectile Defaults ---
        private const float DEFAULT_SPEED = 40f;
        private const float DEFAULT_DAMAGE = 25f;
        private const float DEFAULT_LIFETIME = 3f;
        private const float DEFAULT_HOMING_TURN_SPEED = 25f;
        private const float DEFAULT_DETECTION_RADIUS = 40f;

        [Header("Projectile Typology")]
        public ProjectileType type = ProjectileType.Standard;
        [Tooltip("Si se activa, el proyectil elegirá un tipo al azar al despertar.")]
        public bool autoRandomize = false;

        [Header("Manual Overrides (useOverride = false -> Usar Balance Interno)")]
        public Optional<float> speedOverride;
        public Optional<float> damageOverride;
        public Optional<float> lifeTimeOverride;
        public Color color = Color.red;

        [Header("Self-Sufficiency (Child Visuals)")]
        public GameObject visualsStandard;
        public GameObject visualsLaser;
        public GameObject visualsExplosive;
        public GameObject visualsHoming;

        [Header("Explosive Settings")]
        public float explosionRadius = 4f;
        public ParticleSystem explosionVFX;
        public ParticleSystem impactVFX;

        [Header("Target Lock & Guidance Overrides")]
        public Optional<float> homingTurnSpeedOverride;
        public Optional<float> detectionRadiusOverride;

        private Vector3 m_Direction;
        private GameObject m_Owner;
        private Team m_OwnerTeam;
        private bool m_HasHit = false;
        private Transform m_HomingTarget;

        private static Material _sharedMaterial;

        // --- Effective Statistics Resolvers with Optional Protection ---

        public float EffectiveSpeed => speedOverride.GetValue(type == ProjectileType.Laser ? DEFAULT_SPEED * 2.5f : DEFAULT_SPEED);
        public float EffectiveDamage => damageOverride.GetValue(DEFAULT_DAMAGE);
        public float EffectiveLifeTime => lifeTimeOverride.GetValue(DEFAULT_LIFETIME);
        public float EffectiveHomingTurnSpeed => homingTurnSpeedOverride.GetValue(DEFAULT_HOMING_TURN_SPEED);
        public float EffectiveDetectionRadius => detectionRadiusOverride.GetValue(DEFAULT_DETECTION_RADIUS);

        void Awake()
        {
            SetupPhysics();

            if (type == ProjectileType.Random || autoRandomize)
            {
                type = (ProjectileType)Random.Range(0, 4);
            }
        }

        private void Start()
        {
            if (visualsStandard == null) visualsStandard = transform.Find("Visuals_Standard")?.gameObject;
            if (visualsLaser == null) visualsLaser = transform.Find("Visuals_Laser")?.gameObject;
            if (visualsExplosive == null) visualsExplosive = transform.Find("Visuals_Explosive")?.gameObject;
            if (visualsHoming == null) visualsHoming = transform.Find("Visuals_Homing")?.gameObject;

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

        public void Launch(GameObject owner, Vector3 direction, float dmg, Team team)
        {
            m_Owner = owner;
            m_Direction = direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
            m_OwnerTeam = team;

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
            if (visualsStandard != null) visualsStandard.SetActive(type == ProjectileType.Standard);
            if (visualsLaser != null) visualsLaser.SetActive(type == ProjectileType.Laser);
            if (visualsExplosive != null) visualsExplosive.SetActive(type == ProjectileType.Explosive);
            if (visualsHoming != null) visualsHoming.SetActive(type == ProjectileType.Homing);

            if (visualsStandard == null && visualsLaser == null && visualsExplosive == null && visualsHoming == null)
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
            core.transform.localScale = (type == ProjectileType.Laser) ? new Vector3(0.1f, 0.1f, 0.8f) : Vector3.one * 0.35f;

            var c = core.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);

            if (_sharedMaterial == null)
                _sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));

            var mr = core.GetComponent<MeshRenderer>();
            mr.material = _sharedMaterial;
            mr.material.color = color;

            TrailRenderer trail = core.AddComponent<TrailRenderer>();
            if (trail != null)
            {
                trail.time = 0.25f;
                trail.startWidth = 0.15f;
                trail.endWidth = 0f;
                trail.material = mr.material;
                trail.startColor = color;
                trail.endColor = new Color(color.r, color.g, color.b, 0f);
            }
        }

        private void Update()
        {
            GuideTowardsRivalTarget();

            float moveDistance = EffectiveSpeed * Time.deltaTime;

            // Detección continua (SphereCast Sweep) en TODOS los layers (~0) incluyendo capas personalizadas
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

                    if (type == ProjectileType.Explosive) PerformExplosion();
                    else if (targetHealth != null) ApplyDirectDamage(targetHealth);

                    FinalizeImpact();
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

                // Escaneo en TODOS los layers (~0)
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
            if (m_HasHit) return;
            if (m_Owner != null && (other.gameObject == m_Owner || other.transform.IsChildOf(m_Owner.transform))) return;

            HealthController targetHealth = other.GetComponentInParent<HealthController>() ?? other.GetComponent<HealthController>();

            if (other.isTrigger && targetHealth == null) return;

            m_HasHit = true;

            if (type == ProjectileType.Explosive) PerformExplosion();
            else if (targetHealth != null) ApplyDirectDamage(targetHealth);

            FinalizeImpact();
        }

        private void ApplyDirectDamage(HealthController target)
        {
            if (target == null) return;
            if (target.gameObject == m_Owner || (m_Owner != null && target.transform.IsChildOf(m_Owner.transform))) return;

            target.TakeDamage((int)EffectiveDamage);
            //Debug.Log($"<color=red>[Impacto Directo]</color> {gameObject.name} infligió {(int)EffectiveDamage} de daño a {target.gameObject.name}. HP restante: {target.CurrentHP}");
        }

        private void PerformExplosion()
        {
            if (explosionVFX != null) Instantiate(explosionVFX, transform.position, Quaternion.identity);

            Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius, ~0, QueryTriggerInteraction.Collide);
            foreach (var h in hits)
            {
                HealthController hc = h.GetComponentInParent<HealthController>() ?? h.GetComponent<HealthController>();
                if (hc != null && hc.gameObject != m_Owner) ApplyDirectDamage(hc);
            }
        }

        private void FinalizeImpact()
        {
            if (impactVFX != null) Instantiate(impactVFX, transform.position, Quaternion.identity);

            if (IsServer && IsSpawned) NetworkObject.Despawn();
            else Destroy(gameObject);
        }
    }
}
