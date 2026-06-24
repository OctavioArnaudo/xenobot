using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class melee : NetworkBehaviour
{
    [Header("Attack")]
    public float attackRange = 2f;
    public int attackDamage = 25;
    public LayerMask enemyLayer;

    public void OnAttack(InputValue value)
    {
        if (!value.isPressed) return;
        if (IsSpawned && !IsOwner) return; // Solo el dueño del player ataca

        Vector3 attackCenter = transform.position + transform.forward * attackRange;
        Collider[] hits = Physics.OverlapSphere(attackCenter, attackRange, enemyLayer);

        foreach (Collider hit in hits)
        {
            var health = hit.GetComponentInParent<enemyHealth>();
            if (health != null)
                health.RequestDamageRpc(attackDamage);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 attackCenter = transform.position + transform.forward * attackRange;
        Gizmos.DrawWireSphere(attackCenter, attackRange);
    }
}