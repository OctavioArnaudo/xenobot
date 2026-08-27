using UnityEngine;
using Unity.Netcode;
using Xenobot.ModularCombat;

// IA del enemigo (esfera). Un solo script para ambos tipos: Melee o Ranged.
// Funciona tanto en red (solo el servidor ejecuta la lógica) como en offline.
public class EnemyAI : NetworkBehaviour
{
    public enum AttackType { Melee, Ranged }

    [Header("Tipo")]
    public AttackType attackType = AttackType.Melee;

    [Header("Movimiento")]
    public float wanderSpeed = 2f;
    public float chaseSpeed = 4f;
    public float wanderChangeInterval = 2f;

    [Header("Detección")]
    public float detectionRange = 8f;

    [Header("Ataque")]
    public float attackRange = 2f;
    public float attackCooldown = 1.5f;
    public int attackDamage = 10;

    Transform m_Target;
    Vector3 m_WanderDir;
    float m_WanderTimer;
    float m_AttackTimer;

    ClickToShoot m_Shooter;
    CombatTeamMember m_TeamMember;

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool CanExecuteLogic => !IsNetworkActive || IsServer;

    void Awake()
    {
        m_Shooter = GetComponent<ClickToShoot>();
        m_TeamMember = GetComponent<CombatTeamMember>();
        if (m_TeamMember == null)
            m_TeamMember = gameObject.AddComponent<CombatTeamMember>();

        m_TeamMember.Team = CombatTeam.Enemy;
    }

    void Start()
    {
        // En modo offline, inicializamos aquí
        if (!IsNetworkActive) PickNewWanderDirection();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        PickNewWanderDirection();
    }

    void Update()
    {
        // Solo ejecuta la lógica si es offline o si es el servidor en online
        if (!CanExecuteLogic) return;

        FindTarget();

        if (m_Target != null)
            ChaseAndAttack();
        else
            Wander();
    }

    void FindTarget()
    {
        // Busca al combatiente más cercano que no sea del mismo equipo
        if (m_Target != null)
        {
            float d = Vector3.Distance(transform.position, m_Target.position);
            if (d > detectionRange) m_Target = null;
            else return;
        }

        var members = FindObjectsByType<CombatTeamMember>(FindObjectsSortMode.None);
        float closest = detectionRange;
        foreach (var m in members)
        {
            if (m.Team == m_TeamMember.Team || m.Team == CombatTeam.Neutral) continue;

            float d = Vector3.Distance(transform.position, m.transform.position);
            if (d <= closest)
            {
                closest = d;
                m_Target = m.transform;
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
            CombatDamage.TryApply(m_Target.gameObject, attackDamage, gameObject);
        }
        else
        {
            if (m_Shooter != null)
                m_Shooter.FireAt(m_Target.position);
        }
    }

    // Punto de entrada para dañar al player, resuelve según contexto (online/offline) en PlayerHealth.
    public static void DamagePlayer(Transform player, int damage)
    {
        CombatDamage.TryApply(player.gameObject, damage, null);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
