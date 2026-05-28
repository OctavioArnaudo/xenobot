using UnityEngine;
using UnityEngine.InputSystem;

public class melee : MonoBehaviour
{
    [Header("Attack")]
    public float attackRange = 2f;
    public int attackDamage = 25;
    public LayerMask enemyLayer;

    public void OnAttack(InputValue value)
    {
        if (!value.isPressed) return;

        Vector3 attackCenter = transform.position + transform.forward * attackRange;

        Collider[] hits = Physics.OverlapSphere(
            attackCenter,
            attackRange,
            enemyLayer
        );

        foreach (Collider hit in hits)
        {
            Debug.Log("Golpeaste a: " + hit.name);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;

        Vector3 attackCenter = transform.position + transform.forward * attackRange;

        Gizmos.DrawWireSphere(attackCenter, attackRange);
    }
}