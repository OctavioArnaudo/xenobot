using UnityEngine;
using UnityEngine.AI;

namespace Xenobot.ModularCombat
{
    [RequireComponent(typeof(ClickToShoot))]
    public class BasicAttackAI : MonoBehaviour
    {
        public string TargetTag = "Player";
        public float DetectionRange = 14f;
        public float AttackRange = 8f;
        public float MeleeRange = 1.6f;
        public float MoveSpeed = 3.5f;
        public float TurnSpeed = 10f;
        public float MeleeDamage = 12f;
        public float MeleeCooldown = 1.2f;
        public bool UseRangedAttack = true;

        ClickToShoot m_Shooter;
        NavMeshAgent m_Agent;
        Transform m_Target;
        float m_NextMeleeTime;

        void Awake()
        {
            m_Shooter = GetComponent<ClickToShoot>();
            m_Shooter.UsePlayerInput = false;
            m_Agent = GetComponent<NavMeshAgent>();

            if (m_Agent != null)
                m_Agent.speed = MoveSpeed;
        }

        void Update()
        {
            FindTarget();
            if (m_Target == null)
                return;

            Vector3 targetPosition = m_Target.position;
            float distance = Vector3.Distance(transform.position, targetPosition);

            RotateTowards(targetPosition);

            if (distance > AttackRange)
            {
                MoveTo(targetPosition);
                return;
            }

            StopMoving();

            if (UseRangedAttack)
                m_Shooter.FireAt(targetPosition + Vector3.up);
            else if (distance <= MeleeRange && Time.time >= m_NextMeleeTime)
                MeleeAttack();
        }

        void FindTarget()
        {
            if (m_Target != null && Vector3.Distance(transform.position, m_Target.position) <= DetectionRange)
                return;

            m_Target = null;
            GameObject[] candidates = GameObject.FindGameObjectsWithTag(TargetTag);
            float closestDistance = DetectionRange;

            for (int i = 0; i < candidates.Length; i++)
            {
                float distance = Vector3.Distance(transform.position, candidates[i].transform.position);
                if (distance <= closestDistance)
                {
                    closestDistance = distance;
                    m_Target = candidates[i].transform;
                }
            }
        }

        void MoveTo(Vector3 position)
        {
            if (m_Agent != null && m_Agent.isOnNavMesh)
            {
                m_Agent.isStopped = false;
                m_Agent.SetDestination(position);
                return;
            }

            Vector3 direction = Vector3.ProjectOnPlane(position - transform.position, Vector3.up).normalized;
            transform.position += direction * MoveSpeed * Time.deltaTime;
        }

        void StopMoving()
        {
            if (m_Agent != null && m_Agent.isOnNavMesh)
                m_Agent.isStopped = true;
        }

        void RotateTowards(Vector3 position)
        {
            Vector3 direction = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
            if (direction.sqrMagnitude <= 0.001f)
                return;

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, TurnSpeed * Time.deltaTime);
        }

        void MeleeAttack()
        {
            m_NextMeleeTime = Time.time + MeleeCooldown;
            CombatDamage.TryApply(m_Target.gameObject, MeleeDamage, gameObject);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, DetectionRange);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, AttackRange);
        }
    }
}
