using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace Combating.Scripts
{
    public class EnemyController : NetworkBehaviour
    {
        public enum AttackType { Melee, Ranged }

        [Header("AI Config")]
        public AttackType attackType = AttackType.Ranged;
        [SerializeField] private string playerTag = "Player"; // Cambiado para coincidir con la escena

        [Header("Movement")]
        public float hoverHeight = 3.5f; // Altura para sobrevolar el piso
        public float wanderSpeed = 2f;
        public float chaseSpeed = 4f;
        public float turnSpeed = 10f;
        public float wanderRadius = 10f;

        [Header("Combat Ranges")]
        public float detectionRange = 18f;
        public float chaseRange = 10f; // Añadido para coincidir con la escena
        public float attackRange = 2f;  // Ajustado al valor común de la escena
        public float meleeRange = 2.5f;
        public float attackCooldown = 1.5f;

        private NavMeshAgent m_Agent;
        private Transform m_Target;
        private ShootController m_Shooter;
        private MeleeController m_Melee;
        private HealthController m_Health;
        private float m_AttackTimer;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool CanExecuteLogic => !IsNetworkActive || IsServer;

        void Awake()
        {
            m_Agent = GetComponent<NavMeshAgent>();

            // Zero-Dependency Bootstrapping: Agregar NavMeshAgent si falta
            if (m_Agent == null)
            {
                m_Agent = gameObject.AddComponent<NavMeshAgent>();
                Debug.Log($"[EnemyController] {gameObject.name}: NavMeshAgent agregado automáticamente.");
            }

            // Aplicar altura de sobrevuelo
            if (m_Agent != null)
            {
                m_Agent.baseOffset = hoverHeight;
                m_Agent.updateRotation = false; // Deshabilitar rotacion automatica para manejar Pitch/Yaw manualmente
            }

            m_Shooter = GetComponent<ShootController>();
            m_Melee = GetComponent<MeleeController>();
            m_Health = GetComponent<HealthController>();

            if (m_Shooter == null && attackType == AttackType.Ranged) Debug.LogWarning($"[EnemyController] {gameObject.name} no tiene ShootController para atacar a distancia.");
            if (m_Melee == null && attackType == AttackType.Melee) Debug.LogWarning($"[EnemyController] {gameObject.name} no tiene MeleeController para ataque cuerpo a cuerpo.");

            if (m_Shooter != null) m_Shooter.UsePlayerInput = false;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) enabled = false;
        }

        void Update()
        {
            if (!CanExecuteLogic) return;
            if (m_Health != null && m_Health.CurrentHP <= 0) return;

            FindTarget();

            if (m_Target != null)
                ChaseAndAttack();
            else
                Wander();
        }

        void FindTarget()
        {
            if (m_Target != null)
            {
                if (Vector3.Distance(transform.position, m_Target.position) > detectionRange)
                    m_Target = null;
                else return;
            }

            var players = GameObject.FindGameObjectsWithTag(playerTag);
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

        void ChaseAndAttack()
        {
            float distance = Vector3.Distance(transform.position, m_Target.position);
            m_AttackTimer -= Time.deltaTime;

            RotateTowards(m_Target.position);

            float currentRange = (attackType == AttackType.Melee) ? meleeRange : attackRange;

            if (distance > currentRange)
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

        void Wander()
        {
            if (m_Agent == null || !m_Agent.isOnNavMesh || m_Agent.pathPending || m_Agent.remainingDistance > 0.5f) return;

            Vector3 randomPos = transform.position + Random.insideUnitSphere * wanderRadius;
            if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, wanderRadius, 1))
            {
                MoveTo(hit.position, wanderSpeed);
            }

            // Rotar hacia donde se mueve el agente
            if (m_Agent.velocity.sqrMagnitude > 0.1f)
            {
                RotateTowards(transform.position + m_Agent.velocity);
            }
        }

        private void PerformAttack()
        {
            if (m_Target == null) return;
            if (attackType == AttackType.Melee)
            {
                if (m_Melee != null) m_Melee.PerformMeleeAction();
            }
            else
            {
                if (m_Shooter != null) m_Shooter.FireAt(m_Target.position + Vector3.up);
            }
        }

        void MoveTo(Vector3 position, float speed)
        {
            if (m_Agent != null && m_Agent.isOnNavMesh)
            {
                m_Agent.speed = speed;
                m_Agent.isStopped = false;
                m_Agent.SetDestination(position);
            }
        }

        void StopMoving()
        {
            if (m_Agent != null && m_Agent.isOnNavMesh) m_Agent.isStopped = true;
        }

        void RotateTowards(Vector3 position)
        {
            Vector3 direction = (position - transform.position).normalized;
            if (direction.sqrMagnitude > 0.001f)
            {
                // Permitir rotacion en 3D (Yaw y Pitch) para atacar desde cualquier angulo
                Quaternion rot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, rot, turnSpeed * Time.deltaTime);
            }
        }
    }
}
