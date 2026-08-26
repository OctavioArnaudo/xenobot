using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

// Sistema de ataque cuerpo a cuerpo.
// Funciona en red (via ServerRpc) y en offline (localmente).
public class melee : NetworkBehaviour
{
    [Header("Attack Settings")]
    public float attackRange = 2f;
    public int attackDamage = 25;
    public LayerMask enemyLayer;

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    // El New Input System llamar a esto automticamente
    public void OnAttack(InputValue value)
    {
        if (!value.isPressed) return;

        if (IsNetworkActive)
        {
            // Solo el dueo de este personaje puede iniciar la peticin de ataque
            if (!IsOwner) return;
            RequestAttackServerRpc();
        }
        else
        {
            // Offline: Procesar ataque localmente
            PerformAttack();
        }
    }

    [ServerRpc]
    private void RequestAttackServerRpc()
    {
        // [SERVIDOR] La fsica y el clculo de impacto ocurren aqu
        PerformAttack();
    }

    private void PerformAttack()
    {
        Vector3 attackCenter = transform.position + transform.forward * attackRange;
        Collider[] hits = Physics.OverlapSphere(attackCenter, attackRange, enemyLayer);

        foreach (Collider hit in hits)
        {
            // Buscamos el componente de vida en el enemigo
            var health = hit.GetComponentInParent<enemyHealth>();
            if (health != null)
            {
                // health.TakeDamage ya resuelve si es red o offline
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
