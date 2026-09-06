using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

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
        private static Mesh _sphereMesh;

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
            col.radius = 0.15f;
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
                if (child.name == "Core") Destroy(child.gameObject);
            }

            if (_sharedMaterial == null) {
                _sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            }
            if (_sphereMesh == null) {
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _sphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(temp);
            }

            GameObject core = new GameObject("Core");
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * 0.25f;

            var mf = core.AddComponent<MeshFilter>();
            mf.mesh = _sphereMesh;
            var mr = core.AddComponent<MeshRenderer>();
            mr.material = _sharedMaterial;
            mr.material.color = color;

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
            if (m_HasHit) return;

            // Ignore our own owner and their children
            if (m_Owner != null && (other.gameObject == m_Owner || other.transform.IsChildOf(m_Owner.transform))) return;

            // Check if target has health
            var targetHealth = other.GetComponentInParent<PlayerController>();

            // If it's a trigger but has no health, ignore it (it's likely a zone or another projectile)
            if (other.isTrigger && targetHealth == null) return;

            if (targetHealth != null)
            {
                // Friendly fire check
                if (targetHealth.MyTeam == m_OwnerTeam && m_OwnerTeam != Team.Neutral) return;

                m_HasHit = true;

                var damageCtrl = targetHealth.GetComponent<DamageController>();
                if (damageCtrl != null) damageCtrl.TakeDamage((int)damage, m_OwnerTeam);
                else targetHealth.ApplyHealthChangeServerRpc(-(int)damage);
            }
            else
            {
                // Hit a non-trigger solid object
                if (!other.isTrigger) m_HasHit = true;
            }

            if (m_HasHit) FinalizeImpact();
        }

        private void FinalizeImpact()
        {
            CreateImpactEffect();
            if (IsServer && IsSpawned) NetworkObject.Despawn();
            else Destroy(gameObject);
        }

        private void CreateImpactEffect()
        {
            GameObject burst = new GameObject("VFX");
            burst.transform.position = transform.position;
            burst.transform.localScale = Vector3.one * 0.3f;
            var mf = burst.AddComponent<MeshFilter>();
            mf.mesh = _sphereMesh;
            var mr = burst.AddComponent<MeshRenderer>();
            mr.material = _sharedMaterial;
            Destroy(burst, 0.05f);
        }
    }
}
