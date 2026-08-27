using UnityEngine;
using Unity.Netcode;

// IA del enemigo (esfera). Un solo script para ambos tipos: Melee o Ranged.
// Funciona tanto en red (solo el servidor ejecuta la lgica) como en offline.
public class EnemyAI : NetworkBehaviour
{
    public enum AttackType { Melee, Ranged }

    [Header("Tipo")]
    public AttackType attackType = AttackType.Melee;

    [Header("Movimiento")]
    public float wanderSpeed = 2f;
    public float chaseSpeed = 4f;
    public float wanderChangeInterval = 2f;

    [Header("Deteccin")]
    public float detectionRange = 8f;

    [Header("Ataque")]
    public float attackRange = 2f;
    public float attackCooldown = 1.5f;
    public int attackDamage = 10;

    [Header("Ranged (solo si AttackType = Ranged)")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 10f;

    Transform m_Target;
    Vector3 m_WanderDir;
    float m_WanderTimer;
    float m_AttackTimer;

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool CanExecuteLogic => !IsNetworkActive || IsServer;

    void Start()
    {
        // En modo offline, inicializamos aqu
        if (!IsNetworkActive) PickNewWanderDirection();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        PickNewWanderDirection();
    }

    void Update()
    {
        // Solo ejecuta la lgica si es offline o si es el servidor en online
        if (!CanExecuteLogic) return;

        FindTarget();

        if (m_Target != null)
            ChaseAndAttack();
        else
            Wander();
    }

    void FindTarget()
    {
        // Busca al jugador ms cercano dentro del rango de deteccin
        if (m_Target != null)
        {
            float d = Vector3.Distance(transform.position, m_Target.position);
            if (d > detectionRange) m_Target = null;
            else return;
        }

        var players = GameObject.FindGameObjectsWithTag("Player");
        float closest = detectionRange;
        foreach (var p in players)
        {
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d <= closest)
            {
                closest = d;
                m_Target = p.transform;
            }
        }
    }

    void Wander()
    {
        m_WanderTimer -= Time.deltaTime;
        if (m_WanderTimer <= 0f) PickNewWanderDirection();

        transform.position += m_WanderDir * wanderSpeed * Time.deltaTime;
    }

    void PickNewWanderDirection()
    {
        Vector2 rnd = Random.insideUnitCircle.normalized;
        m_WanderDir = new Vector3(rnd.x, 0f, rnd.y);
        m_WanderTimer = wanderChangeInterval;
    }

    void ChaseAndAttack()
    {
        float distance = Vector3.Distance(transform.position, m_Target.position);
        m_AttackTimer -= Time.deltaTime;

        if (distance > attackRange)
        {
            Vector3 dir = (m_Target.position - transform.position).normalized;
            transform.position += dir * chaseSpeed * Time.deltaTime;
        }
        else if (m_AttackTimer <= 0f)
        {
            Attack();
            m_AttackTimer = attackCooldown;
        }
    }

    void Attack()
    {
        if (attackType == AttackType.Melee)
        {
            DamagePlayer(m_Target, attackDamage);
        }
        else
        {
            FireProjectile();
        }
    }

    // Punto de entrada para daar al player, resuelve segn contexto (online/offline) en PlayerHealth.
    public static void DamagePlayer(Transform player, int damage)
    {
        var health = player.GetComponent<PlayerHealth>();
        if (health != null) health.TakeDamage(damage);
    }

    void FireProjectile()
    {
        if (projectilePrefab == null || m_Target == null) return;

        Vector3 dir = (m_Target.position - transform.position).normalized;
        var go = Instantiate(projectilePrefab, transform.position + dir, Quaternion.LookRotation(dir));

        if (IsNetworkActive)
        {
            if (go.TryGetComponent<NetworkObject>(out var netObj))
                netObj.Spawn();
        }

        var proj = go.GetComponent<EnemyProjectile>();
        if (proj != null) proj.Launch(dir, projectileSpeed, attackDamage);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
