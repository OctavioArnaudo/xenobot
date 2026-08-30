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

        public void Launch(GameObject owner, Vector3 direction, float dmg, Team team)
        {
            m_Owner = owner;
            m_Direction = direction.normalized;
            damage = dmg;
            m_OwnerTeam = team;
            SetupVisuals();

            if (IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                Destroy(gameObject, lifeTime);
        }

        private void SetupVisuals()
        {
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * 0.25f;
            Destroy(core.GetComponent<Collider>());

            var mr = core.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            mat.color = color;
            mr.material = mat;

            var trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.2f;
            trail.startWidth = 0.15f;
            trail.endWidth = 0f;
            trail.material = mat;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
        }

        private void Update()
        {
            transform.position += m_Direction * speed * Time.deltaTime;
        }

        private void OnTriggerEnter(Collider other)
        {
            var targetHealth = other.GetComponentInParent<HealthController>();

            if (targetHealth != null)
            {
                // Solo ignorar si es el MISMO equipo y ese equipo NO es Neutral
                if (targetHealth.team == m_OwnerTeam && m_OwnerTeam != Team.Neutral) return;

                targetHealth.TakeDamage((int)damage);
                Debug.Log($"[Projectile] Hit {other.gameObject.name}. Damage: {damage}");
            }

            CreateImpactEffect();
            if (IsServer && IsSpawned) NetworkObject.Despawn();
            else Destroy(gameObject);
        }

        private void CreateImpactEffect()
        {
            GameObject burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            burst.transform.position = transform.position;
            burst.transform.localScale = Vector3.one * 0.4f;
            var mr = burst.GetComponent<MeshRenderer>();
            mr.material.color = Color.white;
            Destroy(burst, 0.1f);
        }
    }
}
