using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// AI Logic Controller.
    /// Manages Patrol, Pursuit, and Decision Making.
    /// Delegates Combat to ShootController and/or MeleeController if they exist.
    /// </summary>
    public class EnemyController : NetworkBehaviour
    {
        public enum AIState { Patrol, Chase, Attack }

        [Header("AI Config")]
        public AIState currentState = AIState.Patrol;
        [SerializeField] private string playerTag = "Player";

        [Header("Movement")]
        public float hoverHeight = 3.5f;
        public float wanderSpeed = 2f;
        public float chaseSpeed = 5f;
        public float turnSpeed = 10f;
        public float wanderRadius = 15f;

        [Header("AI Perception & Ranges")]
        public float detectionRange = 20f;
        public float shootRange = 12f;
        public float meleeRange = 2.5f;

        [Header("Combat (Optional Controllers)")]
        public ShootController m_Shooter;
        public MeleeController m_Melee;
        public FuelController m_Health;
        public SpawnController m_Spawn;

        private NavMeshAgent m_Agent;
        private Transform m_Target;
        private Vector3 _startPosition;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool CanExecuteLogic => !IsNetworkActive || IsServer;

        void Awake()
        {
            _startPosition = transform.position;
            m_Agent = GetComponentInChildren<NavMeshAgent>() ?? gameObject.AddComponent<NavMeshAgent>();

            if (m_Agent != null)
            {
                m_Agent.baseOffset = hoverHeight;
                m_Agent.updateRotation = false;
            }

            // Auto-detección opcional de módulos
            if (m_Shooter == null) m_Shooter = GetComponent<ShootController>();
            if (m_Melee == null) m_Melee = GetComponent<MeleeController>();
            if (m_Health == null) m_Health = GetComponent<FuelController>();
            if (m_Spawn == null) m_Spawn = GetComponent<SpawnController>();

            if (m_Shooter != null) m_Shooter.UsePlayerInput = false;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) enabled = false;
        }

        void Update()
        {
            if (!CanExecuteLogic) return;

            if (m_Health != null && m_Health.CurrentHP <= 0)
            {
                StopMoving();
                return;
            }

            FindTarget();
            UpdateAIState();
        }

        void FindTarget()
        {
            if (m_Target != null)
            {
                if (Vector3.Distance(transform.position, m_Target.position) > detectionRange)
                    m_Target = null;
            }

            if (m_Target == null)
            {
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
        }

        void UpdateAIState()
        {
            if (m_Target != null)
            {
                float distance = Vector3.Distance(transform.position, m_Target.position);

                // Determinamos si el enemigo puede atacar basándonos en los controladores presentes
                bool canShoot = m_Shooter != null && distance <= shootRange;
                bool canMelee = m_Melee != null && distance <= meleeRange;

                if (canShoot || canMelee)
                {
                    currentState = AIState.Attack;
                    ExecuteCombat(canShoot, canMelee);

                    // Si solo puede disparar pero está lejos de melee, seguimos moviéndonos si es necesario
                    // o nos detenemos según la lógica de ataque.
                    // Por ahora, el ataque detiene el movimiento de navegación para precisión.
                    StopMoving();
                }
                else
                {
                    currentState = AIState.Chase;
                    MoveTo(m_Target.position, chaseSpeed);
                    RotateBaseTowards(m_Target.position);
                }
            }
            else
            {
                currentState = AIState.Patrol;
                Wander();
            }
        }

        void Wander()
        {
            if (m_Agent == null || !m_Agent.isOnNavMesh || m_Agent.pathPending || m_Agent.remainingDistance > 1f) return;

            Vector3 randomPos = _startPosition + Random.insideUnitSphere * wanderRadius;
            if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, wanderRadius, 1))
            {
                MoveTo(hit.position, wanderSpeed);
            }

            if (m_Agent.velocity.sqrMagnitude > 0.1f)
            {
                RotateBaseTowards(transform.position + m_Agent.velocity);
            }
        }

        void ExecuteCombat(bool canShoot, bool canMelee)
        {
            if (m_Target == null) return;

            RotateBaseTowards(m_Target.position);

            // Delegación directa: Cada controlador sabe qué hacer
            if (canMelee)
            {
                m_Melee.PerformMeleeAction(m_Target.position);
            }

            if (canShoot)
            {
                m_Shooter.FireAt(m_Target.position + Vector3.up);
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

        void RotateBaseTowards(Vector3 position)
        {
            Vector3 direction = (position - transform.position);
            direction.y = 0;

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetYaw = Quaternion.LookRotation(direction.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetYaw, turnSpeed * Time.deltaTime);
            }
        }
    }
}
