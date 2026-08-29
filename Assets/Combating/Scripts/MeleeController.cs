using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// Melee attack system.
    /// Works in Network (via ServerRpc) and Offline (locally).
    /// </summary>
    public class MeleeController : NetworkBehaviour
    {
        [Header("Attack Settings")]
        public float attackRange = 2f;
        public int attackDamage = 25;
        public LayerMask enemyLayer;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        // Called automatically by the New Input System
        public void OnAttack(InputValue value)
        {
            if (!value.isPressed) return;

            if (IsNetworkActive)
            {
                // Only the owner of this character can initiate the attack request
                if (!IsOwner) return;
                RequestAttackServerRpc();
            }
            else
            {
                // Offline: Process attack locally
                PerformAttack();
            }
        }

        [ServerRpc]
        private void RequestAttackServerRpc()
        {
            // [SERVER] Physics and impact calculation occur here
            PerformAttack();
        }

        private void PerformAttack()
        {
            Vector3 attackCenter = transform.position + transform.forward * attackRange;
            Collider[] hits = Physics.OverlapSphere(attackCenter, attackRange, enemyLayer);

            foreach (Collider hit in hits)
            {
                // Look for the spawn controller in the hit object
                var health = hit.GetComponentInParent<SpawnController>();
                if (health != null)
                {
                    // TakeDamage handles both network and offline contexts
                    health.TakeDamage(attackDamage);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Vector3 attackCenter = transform.position + transform.forward * attackRange;
            Gizmos.DrawWireSphere(attackCenter, attackRange);
        }
    }
}
