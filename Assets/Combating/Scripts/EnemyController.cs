using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace Combating.Scripts
{
    public class EnemyController : NetworkBehaviour
    {
        [Header("Detection")]
        public float detectionRange = 18f;
        public float chaseRange = 14f;
        public float attackRange = 12f;
        public string playerTag = "Player";

        [Header("Movement")]
        public float wanderSpeed = 2f;
        public float chaseSpeed = 5f;
        public float wanderRadius = 10f;

        private NavMeshAgent m_Agent;
        private Transform m_Target;
        private ShootController m_Shooter;
        private MeleeController m_Melee;
        private HealthController m_Health;

        private void Awake()
        {
            m_Agent = GetComponent<NavMeshAgent>();
            m_Shooter = GetComponent<ShootController>();
            m_Melee = GetComponent<MeleeController>();
            m_Health = GetComponent<HealthController>();

            if (m_Shooter != null) m_Shooter.UsePlayerInput = false;
        }

        private void Update()
        {
            if (IsNetworkActive && !IsServer) return;
            if (m_Health != null && m_Health.CurrentHP <= 0) return;

            FindTarget();

            if (m_Target != null)
            {
                float dist = Vector3.Distance(transform.position, m_Target.position);
                RotateTowards(m_Target.position);

                if (dist <= attackRange)
                {
                    StopMoving();
                    PerformAttack();
                }
                else
                {
                    ChaseTarget();
                }
            }
            else
            {
                Wander();
            }
        }

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private void FindTarget()
        {
            if (m_Target != null)
            {
                float d = Vector3.Distance(transform.position, m_Target.position);
                if (d > detectionRange) m_Target = null;
                else return;
            }

            var players = GameObject.FindGameObjectsWithTag(playerTag);
            float closestDist = detectionRange;

            foreach (var p in players)
            {
                float d = Vector3.Distance(transform.position, p.transform.position);
                if (d < closestDist)
                {
                    closestDist = d;
                    m_Target = p.transform;
                    Debug.Log($"[EnemyController] {gameObject.name} targeting: {p.name}");
                }
            }
        }

        private void ChaseTarget()
        {
            if (m_Agent == null || m_Target == null || !m_Agent.isOnNavMesh) return;
            m_Agent.isStopped = false;
            m_Agent.speed = chaseSpeed;
            m_Agent.SetDestination(m_Target.position);
        }

        private void Wander()
        {
            if (m_Agent == null || !m_Agent.isOnNavMesh || m_Agent.pathPending || m_Agent.remainingDistance > 0.5f) return;

            m_Agent.isStopped = false;
            m_Agent.speed = wanderSpeed;
            Vector3 randomPos = transform.position + Random.insideUnitSphere * wanderRadius;
            if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, wanderRadius, 1))
                m_Agent.SetDestination(hit.position);
        }

        private void StopMoving()
        {
            if (m_Agent != null && m_Agent.isOnNavMesh) m_Agent.isStopped = true;
        }

        private void RotateTowards(Vector3 position)
        {
            Vector3 direction = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion rot = Quaternion.LookRotation(direction.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 8f);
            }
        }

        private void PerformAttack()
        {
            if (m_Shooter != null)
            {
                m_Shooter.FireAt(m_Target.position + Vector3.up);
            }
            else if (m_Melee != null)
            {
                m_Melee.PerformMeleeAction();
            }
        }
    }
}
