using UnityEngine;
using Unity.Netcode;

namespace Xenobot.ModularCombat
{
    public class CombatProjectile : NetworkBehaviour
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

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        public void Launch(GameObject owner, Vector3 direction, float damage, float speed)
        {
            m_Owner = owner;
            m_Direction = direction.sqrMagnitude > 0f ? direction.normalized : transform.forward;
            Damage = damage;
            Speed = speed;
            transform.forward = m_Direction;
            m_LastPosition = transform.position;
            m_OwnerColliders = owner != null ? owner.GetComponentsInChildren<Collider>() : null;

            if (!IsNetworkActive)
                Destroy(gameObject, Lifetime);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Invoke(nameof(DespawnProjectile), Lifetime);
            }
        }

        void DespawnProjectile()
        {
            if (IsSpawned)
                NetworkObject.Despawn();
        }

        void OnEnable()
        {
            m_Direction = transform.forward;
            m_LastPosition = transform.position;
        }

        void Update()
        {
            // Movimiento visual en todos los clientes
            Vector3 nextPosition = transform.position + m_Direction * Speed * Time.deltaTime;
            Vector3 movement = nextPosition - m_LastPosition;

            // Solo el servidor o modo offline procesa colisiones
            if (!IsNetworkActive || IsServer)
            {
                if (Physics.SphereCast(m_LastPosition, Radius, movement.normalized, out RaycastHit hit,
                        movement.magnitude, HittableLayers, QueryTriggerInteraction.Collide))
                {
                    if (IsValidHit(hit.collider))
                    {
                        Hit(hit);
                        return;
                    }
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
            if (IsNetworkActive)
            {
                if (IsServer)
                {
                    CombatDamage.TryApply(hit.collider.gameObject, Damage, m_Owner);
                    NotifyHitClientRpc(hit.point, hit.normal);
                    NetworkObject.Despawn();
                }
            }
            else
            {
                CombatDamage.TryApply(hit.collider.gameObject, Damage, m_Owner);
                SpawnImpactVisuals(hit.point, hit.normal);
                Destroy(gameObject);
            }
        }

        [ClientRpc]
        void NotifyHitClientRpc(Vector3 point, Vector3 normal)
        {
            if (!IsServer) // El servidor ya lo hizo o lo hará localmente si no es RPC
                SpawnImpactVisuals(point, normal);
        }

        void SpawnImpactVisuals(Vector3 point, Vector3 normal)
        {
            if (ImpactVfxPrefab != null)
            {
                GameObject impactVfx = Instantiate(ImpactVfxPrefab, point, Quaternion.LookRotation(normal));
                if (ImpactVfxLifetime > 0f)
                    Destroy(impactVfx, ImpactVfxLifetime);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
    }
}
