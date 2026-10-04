using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class MeleeEnemyAI : MonoBehaviour
{
    private enum MeleeState { Approaching, Telegraphing, Attacking, Recovering }

    [Header("Referencias")]
    [SerializeField] private Transform target;
    [SerializeField] private Animator animator;

    [Header("Parámetros de Combate")]
    [SerializeField] private float meleeRange = 2.0f;
    [SerializeField] private float chaseSpeed = 3.5f;
    [SerializeField] private float strafeSpeed = 2.0f;

    [Header("Tiempos de Secuencia")]
    [SerializeField] private float windUpTime = 0.4f;       // Tiempo de telegrafiado / aviso
    [SerializeField] private float attackDuration = 0.3f;   // Duración del golpe activo
    [SerializeField] private float recoveryTime = 1.2f;     // Enfriamiento post-ataque
    [SerializeField] private float backstepDistance = 2.5f; // Distancia de retroceso táctico

    [Header("Detección de Impacto")]
    [SerializeField] private float attackDamage = 15f;
    [SerializeField] private float attackRadius = 1.2f;
    [SerializeField] private Vector3 attackOffset = new Vector3(0, 1, 1);
    [SerializeField] private LayerMask playerLayer;

    // Control interno de estado
    private MeleeState currentState = MeleeState.Approaching;
    private float stateTimer = 0f;
    private NavMeshAgent agent;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        // Buscar al jugador automáticamente por Tag si no se asignó en el Inspector
        if (target == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) target = playerObj.transform;
        }
    }

    private void Update()
    {
        if (target == null) return;

        ExecuteMeleeSequence();
    }

    private void ExecuteMeleeSequence()
    {
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        switch (currentState)
        {
            case MeleeState.Approaching:
                // PASO 1: APROXIMACIÓN
                if (distanceToTarget <= meleeRange)
                {
                    // Detener navegación para iniciar el ataque
                    if (agent.isOnNavMesh) agent.ResetPath();

                    currentState = MeleeState.Telegraphing;
                    stateTimer = windUpTime;

                    if (animator != null) animator.SetTrigger("TelegraphAttack");
                }
                else
                {
                    MoveTo(target.position, chaseSpeed);
                }
                break;

            case MeleeState.Telegraphing:
                // PASO 2: TELEGRAFIADO / AVISO (Rotación y viento previo)
                RotateTowardsTarget();
                stateTimer -= Time.deltaTime;

                if (stateTimer <= 0f)
                {
                    currentState = MeleeState.Attacking;
                    stateTimer = attackDuration;

                    if (animator != null) animator.SetTrigger("ExecuteAttack");
                    PerformHitDetection();
                }
                break;

            case MeleeState.Attacking:
                // PASO 3: IMPACTO (Ventana de daño)
                RotateTowardsTarget();
                stateTimer -= Time.deltaTime;

                if (stateTimer <= 0f)
                {
                    currentState = MeleeState.Recovering;
                    stateTimer = recoveryTime;

                    // Retroceso táctico post-ataque
                    Vector3 retreatDir = (transform.position - target.position).normalized;
                    Vector3 retreatPos = GetValidNavMeshPosition(transform.position + retreatDir * backstepDistance);
                    MoveTo(retreatPos, chaseSpeed * 0.9f);
                }
                break;

            case MeleeState.Recovering:
                // PASO 4: COOLDOWN Y REORGANIZACIÓN
                stateTimer -= Time.deltaTime;

                if (stateTimer <= 0f)
                {
                    currentState = MeleeState.Approaching;
                }
                break;
        }
    }

    private void MoveTo(Vector3 destination, float speed)
    {
        if (agent == null || !agent.isOnNavMesh) return;
        agent.speed = speed;
        agent.SetDestination(destination);
    }

    private void RotateTowardsTarget()
    {
        Vector3 direction = (target.position - transform.position).normalized;
        direction.y = 0;
        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
        }
    }

    private Vector3 GetValidNavMeshPosition(Vector3 samplePos)
    {
        if (NavMesh.SamplePosition(samplePos, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
        {
            return hit.position;
        }
        return transform.position;
    }

    private void PerformHitDetection()
    {
        Vector3 hitCenter = transform.TransformPoint(attackOffset);
        Collider[] hits = Physics.OverlapSphere(hitCenter, attackRadius, playerLayer);

        foreach (Collider hit in hits)
        {
            // Intentar infligir daño si el objeto tiene un componente de salud o interfaz
            Debug.Log($"¡Golpe melé acertado en: {hit.name}!");
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Visualizar el área de impacto en la vista de Escena de Unity
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.TransformPoint(attackOffset), attackRadius);
    }
}