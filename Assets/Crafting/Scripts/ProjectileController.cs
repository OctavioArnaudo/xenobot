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
        [Header("Projectile Typology")]
        public ProjectileType type = ProjectileType.Standard;
        [Tooltip("Si se activa, el proyectil elegirá un tipo al azar al despertar.")]
        public bool autoRandomize = false;

        [Header("Settings")]
        public float speed = 40f;
        public float damage = 25f;
        public float lifeTime = 3f;
        public Color color = Color.red;

        [Header("Self-Sufficiency (Child Visuals)")]
        [Tooltip("Hijo para proyectil estándar (ej: esfera + trail)")]
        public GameObject visualsStandard;
        [Tooltip("Hijo para proyectil láser (ej: cilindro estirado o LineRenderer)")]
        public GameObject visualsLaser;
        [Tooltip("Hijo para proyectil explosivo (ej: esfera con fuego)")]
        public GameObject visualsExplosive;
        [Tooltip("Hijo para proyectil teledirigido (ej: misil o estela de humo)")]
        public GameObject visualsHoming;

        [Header("Explosive Settings")]
        public float explosionRadius = 4f;
        public ParticleSystem explosionVFX;
        public ParticleSystem impactVFX;

        [Header("Homing Settings")]
        public float homingTurnSpeed = 8f;
        public float detectionRadius = 20f;

        private Vector3 m_Direction;
        private GameObject m_Owner;
        private Team m_OwnerTeam;
        private bool m_HasHit = false;
        private Transform m_HomingTarget;

        private static Material _sharedMaterial;
        private static Mesh _sphereMesh;

        void Awake()
        {
            SetupPhysics();

            // Lógica de aleatoriedad para instancias dinámicas
            if (type == ProjectileType.Random || autoRandomize)
            {
                type = (ProjectileType)Random.Range(0, 4);
            }
        }

        private void Start()
        {
            // Autosuficiencia: Intentar encontrar los hijos por nombre si no están asignados
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
                col.radius = 0.2f;
            }
        }

        public void Launch(GameObject owner, Vector3 direction, float dmg, Team team)
        {
            m_Owner = owner;
            m_Direction = direction.normalized;
            damage = dmg;
            m_OwnerTeam = team;

            // Ignorar colisiones con el dueño
            if (m_Owner != null)
            {
                Collider[] ownerCols = m_Owner.GetComponentsInChildren<Collider>();
                Collider myCol = GetComponent<Collider>();
                if (myCol != null)
                {
                    foreach(var oc in ownerCols) Physics.IgnoreCollision(myCol, oc);
                }
            }

            // Ajuste de velocidad según tipo
            if (type == ProjectileType.Laser) speed *= 3f;

            RefreshVisuals();

            if (IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                Destroy(gameObject, lifeTime);
        }

        private void RefreshVisuals()
        {
            // Activar solo el objeto visual correspondiente al tipo elegido
            if (visualsStandard != null) visualsStandard.SetActive(type == ProjectileType.Standard);
            if (visualsLaser != null) visualsLaser.SetActive(type == ProjectileType.Laser);
            if (visualsExplosive != null) visualsExplosive.SetActive(type == ProjectileType.Explosive);
            if (visualsHoming != null) visualsHoming.SetActive(type == ProjectileType.Homing);

            // FALLBACK HARDCODED: Si no hay ningún prefab visual asignado, creamos un core básico
            // Esto permite probar el script aunque el prefab esté vacío.
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
            core.transform.localScale = (type == ProjectileType.Laser) ? new Vector3(0.1f, 0.1f, 0.8f) : Vector3.one * 0.3f;

            // Eliminar colisionador del núcleo (ya está en el root)
            var c = core.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);

            if (_sharedMaterial == null)
                _sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));

            var mr = core.GetComponent<MeshRenderer>();
            mr.material = _sharedMaterial;
            mr.material.color = color;

            // Añadir Trail al objeto core para mayor estabilidad
            TrailRenderer trail = core.AddComponent<TrailRenderer>();
            if (trail != null)
            {
                trail.time = 0.2f;
                trail.startWidth = 0.1f;
                trail.endWidth = 0f;
                trail.material = mr.material;
                trail.startColor = color;
                trail.endColor = new Color(color.r, color.g, color.b, 0f);
            }
        }

        private void Update()
        {
            if (type == ProjectileType.Homing) HandleHoming();

            transform.position += m_Direction * speed * Time.deltaTime;

            // Orientar el proyectil hacia su dirección de movimiento
            if (m_Direction != Vector3.zero)
                transform.forward = m_Direction;
        }

        private void HandleHoming()
        {
            if (m_HomingTarget == null)
            {
                Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius);
                float minD = float.MaxValue;
                foreach(var h in hits)
                {
                    HealthController hc = h.GetComponentInParent<HealthController>();
                    if (hc != null && hc.gameObject != m_Owner && hc.team != m_OwnerTeam)
                    {
                        float d = Vector3.Distance(transform.position, h.transform.position);
                        if (d < minD) { minD = d; m_HomingTarget = h.transform; }
                    }
                }
            }

            if (m_HomingTarget != null)
            {
                Vector3 targetDir = (m_HomingTarget.position - transform.position).normalized;
                m_Direction = Vector3.Slerp(m_Direction, targetDir, Time.deltaTime * homingTurnSpeed);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_HasHit) return;
            if (m_Owner != null && (other.gameObject == m_Owner || other.transform.IsChildOf(m_Owner.transform))) return;

            HealthController targetHealth = other.GetComponentInParent<HealthController>() ?? other.GetComponent<HealthController>();

            // Si es un trigger sin salud (zona, otro proyectil), lo ignoramos
            if (other.isTrigger && targetHealth == null) return;

            m_HasHit = true;

            if (type == ProjectileType.Explosive) PerformExplosion();
            else if (targetHealth != null) ApplyDirectDamage(targetHealth);

            FinalizeImpact();
        }

        private void ApplyDirectDamage(HealthController target)
        {
            // Evitar fuego amigo si no es neutral
            if (target.team == m_OwnerTeam && m_OwnerTeam != Team.Neutral) return;
            target.TakeDamage((int)damage);
        }

        private void PerformExplosion()
        {
            if (explosionVFX != null) Instantiate(explosionVFX, transform.position, Quaternion.identity);

            Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius);
            foreach(var h in hits)
            {
                HealthController hc = h.GetComponentInParent<HealthController>();
                if (hc != null && hc.team != m_OwnerTeam) ApplyDirectDamage(hc);
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
