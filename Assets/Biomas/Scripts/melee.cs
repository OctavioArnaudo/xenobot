using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class melee : NetworkBehaviour
{
    [Header("Attack Settings")]
    public float attackRange = 2f;
    public int attackDamage = 25;
    public LayerMask enemyLayer;

    // El New Input System llamará a esto automáticamente (Send Messages / Broadcast Messages)
    public void OnAttack(InputValue value)
    {
        if (!value.isPressed) return;

        // Solo el dueño de este personaje puede iniciar la petición de ataque
        if (!IsOwner) return;

        // Le pedimos al servidor que procese el ataque de forma segura
        RequestAttackServerRpc();
    }

    [ServerRpc]
    private void RequestAttackServerRpc()
    {
        // [SERVIDOR] La física y el cálculo de impacto ocurren aquí
        Vector3 attackCenter = transform.position + transform.forward * attackRange;
        Collider[] hits = Physics.OverlapSphere(attackCenter, attackRange, enemyLayer);

        foreach (Collider hit in hits)
        {
            // Buscamos el componente de vida en el enemigo
            var health = hit.GetComponentInParent<enemyHealth>();
            if (health != null)
            {
                // El servidor le resta vida directamente sin intermediarios
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