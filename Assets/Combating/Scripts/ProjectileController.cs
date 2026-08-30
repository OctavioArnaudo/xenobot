using UnityEngine;
using Unity.Netcode;

namespace Combating.Scripts
{
    public class ProjectileController : NetworkBehaviour
    {
        [Header("Settings")]
        public float speed = 40f;
        public float damage = 25f;
        public float lifeTime = 3f;
        public Color color = Color.red;
        public LayerMask hitLayers = ~0;

        private Vector3 m_Direction;
        private GameObject m_Owner;
        private Team m_OwnerTeam;
        private bool m_HasHit = false;
        private static Material _sharedMaterial;

        void Awake()
        {
            SetupPhysics();
        }

        private void SetupPhysics()
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

            SphereCollider col = GetComponent<SphereCollider>();
            if (col == null) col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.2f;
        }

        public void Launch(GameObject owner, Vector3 direction, float dmg, Team team)
        {
            m_Owner = owner;
            m_Direction = direction.normalized;
            damage = dmg;
            m_OwnerTeam = team;

            if (m_Owner != null)
            {
                Collider ownerCol = m_Owner.GetComponentInChildren<Collider>();
                Collider myCol = GetComponent<Collider>();
                if (ownerCol != null && myCol != null) Physics.IgnoreCollision(myCol, ownerCol);
            }

            SetupVisuals();

            if (IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                Destroy(gameObject, lifeTime);
        }

        private void SetupVisuals()
        {
            foreach (Transform child in transform) {
                if (child.name == "ProjectileVisualCore") Destroy(child.gameObject);
            }

            if (_sharedMaterial == null || _sharedMaterial.color != color)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                _sharedMaterial = new Material(shader);
                _sharedMaterial.color = color;
            }

            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "ProjectileVisualCore";
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * 0.25f;
            if (core.TryGetComponent<Collider>(out var c)) Destroy(c);
            core.GetComponent<MeshRenderer>().material = _sharedMaterial;

            TrailRenderer trail = GetComponent<TrailRenderer>();
            if (trail == null) trail = gameObject.AddComponent<TrailRenderer>();

            if (trail != null)
            {
                trail.time = 0.1f;
                trail.startWidth = 0.1f;
                trail.endWidth = 0f;
                trail.material = _sharedMaterial;
                trail.startColor = color;
                trail.endColor = new Color(color.r, color.g, color.b, 0f);
            }
        }

        private void Update()
        {
            transform.position += m_Direction * speed * Time.deltaTime;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_HasHit || other.isTrigger) return;
            if (m_Owner != null && (other.gameObject == m_Owner || other.transform.IsChildOf(m_Owner.transform))) return;

            var targetHealth = other.GetComponentInParent<HealthController>();
            if (targetHealth != null)
            {
                if (targetHealth.team == m_OwnerTeam && m_OwnerTeam != Team.Neutral) return;
                m_HasHit = true;
                targetHealth.TakeDamage((int)damage);
            }
            else
            {
                m_HasHit = true;
            }

            if (m_HasHit) FinalizeImpact();
        }

        private void FinalizeImpact()
        {
            CreateImpactEffect();
            if (IsServer && IsSpawned)
            {
                NetworkObject.Despawn(true); // Los proyectiles siempre son prefabs, aquí sí es seguro el true
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void CreateImpactEffect()
        {
            GameObject burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            burst.name = "VFX_Impact";
            burst.transform.position = transform.position;
            burst.transform.localScale = Vector3.one * 0.3f;
            var mr = burst.GetComponent<MeshRenderer>();
            mr.material = _sharedMaterial; // Reuse material
            Destroy(burst, 0.1f);
        }
    }
}
