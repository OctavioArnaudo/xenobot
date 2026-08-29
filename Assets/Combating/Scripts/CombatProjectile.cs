using UnityEngine;

namespace Combating.Scripts
{
    public class CombatProjectile : MonoBehaviour
    {
        public float Speed = 35f;
        public float Damage = 25f;
        public float Radius = 0.08f;
        public float Lifetime = 3f;
        public LayerMask HittableLayers = ~0;
        public GameObject ImpactVfxPrefab;
        public float ImpactVfxLifetime = 2f;

        GameObject m_Owner;
        Vector3 m_Direction;
        Vector3 m_LastPosition;
        Collider[] m_OwnerColliders;

        public void Launch(GameObject owner, Vector3 direction, float damage, float speed)
        {
            m_Owner = owner;
            m_Direction = direction.sqrMagnitude > 0f ? direction.normalized : transform.forward;
            Damage = damage;
            Speed = speed;
            transform.forward = m_Direction;
            m_LastPosition = transform.position;
            m_OwnerColliders = owner != null ? owner.GetComponentsInChildren<Collider>() : null;
        }

        void OnEnable()
        {
            m_Direction = transform.forward;
            m_LastPosition = transform.position;
            Destroy(gameObject, Lifetime);
        }

        void Update()
        {
            Vector3 nextPosition = transform.position + m_Direction * Speed * Time.deltaTime;
            Vector3 movement = nextPosition - m_LastPosition;

            if (Physics.SphereCast(m_LastPosition, Radius, movement.normalized, out RaycastHit hit,
                    movement.magnitude, HittableLayers, QueryTriggerInteraction.Collide))
            {
                if (IsValidHit(hit.collider))
                {
                    Hit(hit);
                    return;
                }
            }

            transform.position = nextPosition;
            m_LastPosition = transform.position;
        }

        bool IsValidHit(Collider hitCollider)
        {
            if (hitCollider == null)
                return false;

            if (m_OwnerColliders != null)
            {
                for (int i = 0; i < m_OwnerColliders.Length; i++)
                {
                    if (hitCollider == m_OwnerColliders[i])
                        return false;
                }
            }

            return !CombatDamage.AreFriendly(m_Owner, hitCollider.gameObject);
        }

        void Hit(RaycastHit hit)
        {
            CombatDamage.TryApply(hit.collider.gameObject, Damage, m_Owner);

            if (ImpactVfxPrefab != null)
            {
                GameObject impactVfx = Instantiate(ImpactVfxPrefab, hit.point, Quaternion.LookRotation(hit.normal));
                if (ImpactVfxLifetime > 0f)
                    Destroy(impactVfx, ImpactVfxLifetime);
            }

            Destroy(gameObject);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
    }
}
