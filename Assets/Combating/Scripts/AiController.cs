using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// AI Brain Module.
    /// Manages Patrol, Pursuit, and Decision Making.
    /// Delegates Combat to other modules via the Hub.
    /// </summary>
    public class AiController : NetworkBehaviour, IModular
    {
        public enum AIState { Patrol, Chase, Attack }

        [Header("AI Config")]
        public AIState currentState = AIState.Patrol;
        [SerializeField] private string playerTag = "Player";

        [Header("Movement")]
        public float hoverHeight = 3.5f;
        public float wanderSpeed = 2f;
        public float chaseSpeed = 5f;
        public float turnSpeed = 25f;
        public float wanderRadius = 15f;

        [Header("AI Perception & Ranges")]
        public float detectionRange = 20f;
        public float shootRange = 12f;
        public float meleeRange = 2.5f;

        private ModularController _hub;
        private NavMeshAgent m_Agent;
        private Transform m_Target;
        private Vector3 _startPosition;

        private ShootController m_Shooter;
        private MeleeController m_Melee;
        private HealthController m_Health;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool CanExecuteLogic => !IsNetworkActive || IsServer;

        public void Bind(ModularController hub)
        {
            _hub = hub;
            _hub.RegisterModule(this);
            OnRefreshModule();
        }

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                m_Shooter = _hub.GetModule<ShootController>();
                m_Melee = _hub.GetModule<MeleeController>();
                m_Health = _hub.GetModule<HealthController>();

                if (m_Shooter != null) m_Shooter.UsePlayerInput = false;
            }
        }

        void Awake()
        {
            _startPosition = transform.position;

            // Fallback if not modularly bound yet
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
            if (_hub != null) Bind(_hub);

            // Buscamos el agente en el Hub o en nosotros mismos
            if (m_Agent == null)
            {
                m_Agent = (_hub != null) ? _hub.GetComponent<NavMeshAgent>() : GetComponent<NavMeshAgent>();
            }

            if (m_Agent != null)
            {
                m_Agent.baseOffset = hoverHeight;
                m_Agent.updateRotation = false;
            }
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

                bool canShoot = m_Shooter != null && distance <= shootRange;
                bool canMelee = m_Melee != null && distance <= meleeRange;

                if (canShoot || canMelee)
                {
                    currentState = AIState.Attack;
                    ExecuteCombat(canShoot, canMelee);
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

            if (canMelee && m_Melee != null)
            {
                m_Melee.PerformMeleeAction(m_Target.position);
            }

            if (canShoot && m_Shooter != null)
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
