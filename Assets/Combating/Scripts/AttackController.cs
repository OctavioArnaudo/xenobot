using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// Unified AI Controller for enemies. Handles wandering, detection, chasing, and attacking (Melee/Ranged).
    /// Works both in Network (Server authority) and Offline modes.
    /// </summary>
    public class AttackController : NetworkBehaviour
    {
        public enum AttackType { Melee, Ranged }

        [Header("AI Type")]
        public AttackType attackType = AttackType.Melee;
        public string targetTag = "Player";

        [Header("Movement")]
        public float wanderSpeed = 2f;
        public float chaseSpeed = 4f;
        public float turnSpeed = 10f;
        public float wanderChangeInterval = 3f;

        [Header("Detection & Combat")]
        public float detectionRange = 14f;
        public float attackRange = 8f;
        public float meleeRange = 2f;
        public float attackDamage = 15f;
        public float attackCooldown = 1.5f;

        [Header("Ranged Settings")]
        public bool useRangedAttack = true;
        public GameObject projectilePrefab;
        public float projectileSpeed = 15f;

        // Internal State
        private ShootController m_Shooter;
        private NavMeshAgent m_Agent;
        private Transform m_Target;
        private Vector3 m_WanderDir;
        private float m_WanderTimer;
        private float m_AttackTimer;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        private bool CanExecuteLogic => !IsNetworkActive || IsServer;

        void Awake()
        {
            m_Shooter = GetComponent<ShootController>();
            if (m_Shooter != null) m_Shooter.UsePlayerInput = false;

            m_Agent = GetComponent<NavMeshAgent>();
        }

        void Start()
        {
            if (!IsNetworkActive) PickNewWanderDirection();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                // Disable on clients to prevent jitter/conflict
                this.enabled = false;
                return;
            }
            PickNewWanderDirection();
        }

        void Update()
        {
            if (!CanExecuteLogic) return;

            FindTarget();

            if (m_Target != null)
                ChaseAndAttack();
            else
                Wander();
        }

        void FindTarget()
        {
            // Keep target if in range
            if (m_Target != null)
            {
                float d = Vector3.Distance(transform.position, m_Target.position);
                if (d > detectionRange) m_Target = null;
                else return;
            }

            // Find closest player
            var players = GameObject.FindGameObjectsWithTag(targetTag);
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

            MoveTo(transform.position + m_WanderDir, wanderSpeed);
        }

        void PickNewWanderDirection()
        {
            Vector2 rnd = Random.insideUnitCircle.normalized;
            m_WanderDir = new Vector3(rnd.x, 0f, rnd.y) * 5f;
            m_WanderTimer = wanderChangeInterval;
        }

        void ChaseAndAttack()
        {
            float distance = Vector3.Distance(transform.position, m_Target.position);
            m_AttackTimer -= Time.deltaTime;

            RotateTowards(m_Target.position);

            if (distance > (attackType == AttackType.Melee ? meleeRange : attackRange))
            {
                MoveTo(m_Target.position, chaseSpeed);
            }
            else
            {
                StopMoving();
                if (m_AttackTimer <= 0f)
                {
                    PerformAttack();
                    m_AttackTimer = attackCooldown;
                }
            }
        }

        private void PerformAttack()
        {
            if (attackType == AttackType.Melee)
            {
                CombatController.TryApply(m_Target.gameObject, attackDamage, gameObject);
            }
            else
            {
                if (m_Shooter != null)
                {
                    m_Shooter.FireAt(m_Target.position + Vector3.up);
                }
                else
                {
                    FireLegacyProjectile();
                }
            }
        }

        private void FireLegacyProjectile()
        {
            if (projectilePrefab == null || m_Target == null) return;

            Vector3 dir = (m_Target.position - transform.position).normalized;
            var go = Instantiate(projectilePrefab, transform.position + dir + Vector3.up, Quaternion.LookRotation(dir));

            if (IsNetworkActive)
            {
                if (go.TryGetComponent<NetworkObject>(out var netObj))
                    netObj.Spawn();
            }

            var proj = go.GetComponent<EnemyProjectile>();
            if (proj != null) proj.Launch(dir, projectileSpeed, (int)attackDamage);
        }

        void MoveTo(Vector3 position, float speed)
        {
            if (m_Agent != null && m_Agent.isOnNavMesh)
            {
                m_Agent.speed = speed;
                m_Agent.isStopped = false;
                m_Agent.SetDestination(position);
                return;
            }

            Vector3 direction = Vector3.ProjectOnPlane(position - transform.position, Vector3.up).normalized;
            transform.position += direction * speed * Time.deltaTime;
        }

        void StopMoving()
        {
            if (m_Agent != null && m_Agent.isOnNavMesh)
                m_Agent.isStopped = true;
        }

        void RotateTowards(Vector3 position)
        {
            Vector3 direction = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
            if (direction.sqrMagnitude <= 0.001f) return;

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, meleeRange);
        }
    }
}
