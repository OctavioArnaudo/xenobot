using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace Combating.Scripts
{
    public enum EnemyCategory
    {
        Melee,
        Ranged,
        Hybrid
    }

    public enum AIArchetype
    {
        CargaDirecta,           // Carga frontal directa a alta velocidad hacia el objetivo
        FlanqueoYCobertura,     // Mantener distancia optima, rodear en arco y buscar cobertura
        CargaFrenetica,         // Embestida furiosa e imparable a 3.0x de velocidad
        GuardiaConEscudo,       // Activar escudo protector y defender una zona fija
        AtaqueYHuida,           // Disparar a distancia y huir velozmente si el jugador se acerca
        InvocadorRefuerzos,     // Permanecer en retaguardia e invocar esbirros aliados
        EmboscadaEnSigilo,      // Esperar inmovil en sigilo y realizar ataque sorpresa explosivo
        MovimientoErratico,     // Desplazamiento impredecible en zigzag a alta velocidad
        SecuenciaMultifase      // Ejecutar los 8 comportamientos secuencialmente a traves de fases
    }

    public enum PhaseTriggerType
    {
        HealthPercentageLessThan,
        HealthAbsoluteLessThan,
        PlayerDistanceLessThan,
        PlayerDistanceGreaterThan,
        PlayerCountGreaterThan,
        AlliesNearbyLessThan,
        TimeInPhaseGreaterThan,
        DamageTakenInPhaseGreaterThan,
        LineOfSight
    }

    public enum TriggerRequirement
    {
        AnyTriggerMode, // OR logic
        AllTriggersMode  // AND logic
    }

    [System.Serializable]
    public class PhaseTrigger
    {
        public PhaseTriggerType triggerType = PhaseTriggerType.HealthPercentageLessThan;
        public float thresholdValue = 50f;
    }

    [System.Serializable]
    public class EnemyPhase
    {
        public string phaseName = "Fase";
        public AIArchetype behaviorArchetype = AIArchetype.CargaDirecta;

        [Header("Category Override (Optional)")]
        public bool overrideCategory = false;
        public EnemyCategory categoryOverride = EnemyCategory.Melee;

        [Header("Phase Multipliers")]
        public float speedMultiplier = 1.0f;
        public float attackCooldownMultiplier = 1.0f;
        public float damageMultiplier = 1.0f;

        [Header("Visual & Audio Feedback")]
        public ParticleSystem phaseEnterVfx;
        public AudioClip phaseEnterSound;

        [Header("Transition Conditions")]
        public TriggerRequirement triggerRequirement = TriggerRequirement.AnyTriggerMode;
        public List<PhaseTrigger> transitionTriggers = new List<PhaseTrigger>();
    }

    /// <summary>
    /// Advanced AI Controller for Xenobot Enemies.
    /// Supports 3 Enemy Categories (Melee, Ranged, Hybrid), customizable Phases with flexible Triggers,
    /// and 9 Category-Agnostic AI Behavior Archetypes with internal hardcoded defaults and manual inspector overrides.
    /// </summary>
    public class EnemyController : NetworkBehaviour
    {
        public enum AIState { Patrulla, Alerta, Persecucion, Ataque, Huida, Guardia, Sigilo }

        private const float DEFAULT_HOVER_HEIGHT = 3.5f;
        private const float DEFAULT_WANDER_SPEED = 3.5f;
        private const float DEFAULT_CHASE_SPEED = 8.0f;
        private const float DEFAULT_TURN_SPEED = 20.0f;
        private const float DEFAULT_WANDER_RADIUS = 20.0f;

        private const float DEFAULT_DETECTION_RANGE = 35.0f;
        private const float DEFAULT_SHOOT_RANGE = 120.0f;
        private const float DEFAULT_MELEE_RANGE = 4.0f;
        private const float DEFAULT_VISION_ANGLE = 120.0f;

        [Header("Core Configuration")]
        public EnemyCategory enemyCategory = EnemyCategory.Hybrid;
        public AIArchetype mainArchetype = AIArchetype.SecuenciaMultifase;
        [SerializeField] private string playerTag = "Player";

        [Header("Phases Configuration")]
        public List<EnemyPhase> phases = new List<EnemyPhase>();
        public int currentPhaseIndex = 0;

        [Header("Current State (Read-Only Info)")]
        public AIState currentState = AIState.Patrulla;
        public string activePhaseName = "Fase Inicial";
        public AIArchetype activeArchetype = AIArchetype.CargaDirecta;

        [Header("Movement Overrides (useOverride = false -> Usar Balance Interno)")]
        public Optional<float> hoverHeight;
        public Optional<float> wanderSpeed;
        public Optional<float> chaseSpeed;
        public Optional<float> turnSpeed;
        public Optional<float> wanderRadius;

        [Header("Perception & Range Overrides (useOverride = false -> Usar Balance Interno)")]
        public Optional<float> detectionRange;
        public Optional<float> shootRange;
        public Optional<float> meleeRange;
        public Optional<float> visionAngleOverride;

        public float EffectiveVisionAngle => visionAngleOverride.GetValue(DEFAULT_VISION_ANGLE);

        // --- Effective Statistics Resolvers with Optional & Fallback Protection ---
        public float EffectiveHoverHeight
        {
            get
            {
                try { return hoverHeight.GetValue(DEFAULT_HOVER_HEIGHT); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] HoverHeight: {ex.Message}"); }
                return DEFAULT_HOVER_HEIGHT;
            }
        }

        public float EffectiveWanderSpeed
        {
            get
            {
                try { return wanderSpeed.GetValue(DEFAULT_WANDER_SPEED); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] WanderSpeed: {ex.Message}"); }
                return DEFAULT_WANDER_SPEED;
            }
        }

        public float EffectiveChaseSpeed
        {
            get
            {
                try { return chaseSpeed.GetValue(DEFAULT_CHASE_SPEED); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] ChaseSpeed: {ex.Message}"); }
                return DEFAULT_CHASE_SPEED;
            }
        }

        public float EffectiveTurnSpeed
        {
            get
            {
                try { return turnSpeed.GetValue(DEFAULT_TURN_SPEED); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] TurnSpeed: {ex.Message}"); }
                return DEFAULT_TURN_SPEED;
            }
        }

        public float EffectiveWanderRadius
        {
            get
            {
                try { return wanderRadius.GetValue(DEFAULT_WANDER_RADIUS); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] WanderRadius: {ex.Message}"); }
                return DEFAULT_WANDER_RADIUS;
            }
        }

        public float EffectiveDetectionRange
        {
            get
            {
                try { return detectionRange.GetValue(DEFAULT_DETECTION_RANGE); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] DetectionRange: {ex.Message}"); }
                return DEFAULT_DETECTION_RANGE;
            }
        }

        public float EffectiveShootRange
        {
            get
            {
                try { return shootRange.GetValue(DEFAULT_SHOOT_RANGE); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] ShootRange: {ex.Message}"); }
                return DEFAULT_SHOOT_RANGE;
            }
        }

        public float EffectiveMaxProjectileReach
        {
            get
            {
                float baseRange = EffectiveShootRange;
                if (m_Shooter != null && m_Shooter.Projectile != null)
                {
                    var proj = m_Shooter.Projectile.GetComponent<ProjectileController>();
                    if (proj != null)
                    {
                        float projDistance = proj.EffectiveSpeed * proj.EffectiveLifeTime;
                        return Mathf.Max(baseRange, projDistance);
                    }
                }
                return baseRange;
            }
        }

        public float EffectiveMeleeRange
        {
            get
            {
                try { return meleeRange.GetValue(DEFAULT_MELEE_RANGE); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] MeleeRange: {ex.Message}"); }
                return DEFAULT_MELEE_RANGE;
            }
        }

        [Header("Combat Controllers (null = Auto-detectar)")]
        public ShootController m_Shooter = null;
        public MeleeController m_Melee = null;
        public HealthController m_Health = null;
        public SpawnController m_Spawn = null;

        private NavMeshAgent m_Agent;
        private Transform m_Target;
        private Vector3 _startPosition;

        // Dynamic State Variables
        private float _phaseStartTime;
        private int _damageTakenInPhase;
        private float _chaosTimer;
        private Vector3 _chaosDirection;
        private float _supportSummonTimer;
        private bool _isStealthActive;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool CanExecuteLogic => !IsNetworkActive || IsServer;

        private Animator m_Animator;
        private static readonly int _animIDSpeed = Animator.StringToHash("Speed");
        private static readonly int _animIDIsGrounded = Animator.StringToHash("isGrounded");
        private bool _hasAnimSpeed;
        private bool _hasAnimGrounded;

        void Awake()
        {
            _startPosition = transform.position;
            m_Agent = GetComponentInChildren<NavMeshAgent>() ?? gameObject.AddComponent<NavMeshAgent>();

            if (m_Agent != null)
            {
                m_Agent.updateRotation = false;
                EnemyCategory cat = GetCurrentCategory();
                m_Agent.baseOffset = (cat == EnemyCategory.Melee) ? 0f : EffectiveHoverHeight;

                if (cat == EnemyCategory.Melee)
                {
                    m_Agent.stoppingDistance = 0.1f;
                }
            }

            m_Animator = GetComponentInChildren<Animator>();
            if (m_Animator != null)
            {
                _hasAnimSpeed = HasParameter(m_Animator, _animIDSpeed);
                _hasAnimGrounded = HasParameter(m_Animator, _animIDIsGrounded);
            }

            // Auto-detect combat controllers
            if (m_Shooter == null) m_Shooter = GetComponent<ShootController>();
            if (m_Melee == null) m_Melee = GetComponent<MeleeController>();
            if (m_Health == null) m_Health = GetComponent<HealthController>();
            if (m_Spawn == null) m_Spawn = GetComponent<SpawnController>();

            if (m_Shooter != null) m_Shooter.UsePlayerInput = false;

            if (m_Health != null)
            {
                m_Health.OnTakeDamage.AddListener(OnDamageTaken);
                m_Health.OnDeath.AddListener(OnEnemyDeath);
            }

            InitializePhasesIfNeeded();
            ValidateRequiredControllers();
        }

        private void InitializePhasesIfNeeded()
        {
            if (phases == null || phases.Count == 0)
            {
                phases = new List<EnemyPhase>();

                if (mainArchetype == AIArchetype.SecuenciaMultifase)
                {
                    // Crear 8 fases en serie representando cada uno de los 8 arquetipos fundamentales
                    AIArchetype[] sequence = new AIArchetype[]
                    {
                        AIArchetype.CargaDirecta,
                        AIArchetype.FlanqueoYCobertura,
                        AIArchetype.CargaFrenetica,
                        AIArchetype.GuardiaConEscudo,
                        AIArchetype.AtaqueYHuida,
                        AIArchetype.InvocadorRefuerzos,
                        AIArchetype.EmboscadaEnSigilo,
                        AIArchetype.MovimientoErratico
                    };

                    for (int i = 0; i < sequence.Length; i++)
                    {
                        EnemyPhase p = new EnemyPhase
                        {
                            phaseName = $"Fase {i + 1}: {sequence[i]}",
                            behaviorArchetype = sequence[i],
                            speedMultiplier = 1.0f + (i * 0.1f)
                        };

                        // Trigger de cambio por porcentaje de vida descendente
                        float hpThreshold = 100f - ((i + 1) * (100f / sequence.Length));
                        p.transitionTriggers.Add(new PhaseTrigger
                        {
                            triggerType = PhaseTriggerType.HealthPercentageLessThan,
                            thresholdValue = Mathf.Max(5f, hpThreshold)
                        });

                        phases.Add(p);
                    }
                }
                else
                {
                    phases.Add(new EnemyPhase
                    {
                        phaseName = $"Fase Única ({mainArchetype})",
                        behaviorArchetype = mainArchetype
                    });
                }
            }

            SetPhase(0);
        }

        private void ValidateRequiredControllers()
        {
            EnemyCategory category = GetCurrentCategory();
            if (category == EnemyCategory.Melee && m_Melee == null)
            {
                Debug.LogError($"<color=red>[EnemyController Error]</color> El enemigo '{gameObject.name}' es de categoría MELEE pero NO tiene MeleeController asignado.");
            }
            if (category == EnemyCategory.Ranged && m_Shooter == null)
            {
                Debug.LogError($"<color=red>[EnemyController Error]</color> El enemigo '{gameObject.name}' es de categoría RANGED pero NO tiene ShootController asignado.");
            }
            if (category == EnemyCategory.Hybrid && (m_Melee == null || m_Shooter == null))
            {
                Debug.LogError($"<color=red>[EnemyController Error]</color> El enemigo '{gameObject.name}' es de categoría HYBRID pero le falta MeleeController o ShootController.");
            }

            AIArchetype archetype = activeArchetype;
            if (archetype == AIArchetype.GuardiaConEscudo)
            {
                var shield = GetComponent<ShieldController>() ?? GetComponentInParent<ShieldController>();
                if (shield == null)
                {
                    Debug.LogError($"<color=red>[EnemyController Error]</color> El arquetipo GUARDIA CON ESCUDO en '{gameObject.name}' REQUIERE un componente ShieldController asignado.");
                }
            }
            else if (archetype == AIArchetype.InvocadorRefuerzos)
            {
                if (m_Spawn == null)
                {
                    Debug.LogError($"<color=red>[EnemyController Error]</color> El arquetipo INVOCADOR REFUERZOS en '{gameObject.name}' REQUIERE un componente SpawnController asignado.");
                }
            }
        }

        private bool HasParameter(Animator animator, int paramHash)
        {
            foreach (AnimatorControllerParameter param in animator.parameters)
                if (param.nameHash == paramHash) return true;
            return false;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) enabled = false;
        }

        private void OnDamageTaken(int damage)
        {
            _damageTakenInPhase += damage;
        }

        private bool _isEnemyDead = false;

        private void OnEnemyDeath()
        {
            if (_isEnemyDead) return;
            _isEnemyDead = true;

            //Debug.Log($"<color=orange>[EnemyAI]</color> {gameObject.name} ha muerto.");

            StopMoving();

            if (m_Spawn != null)
            {
                m_Spawn.TriggerDeath();
                if (this == null) return;
            }

            if (IsNetworkActive && IsServer)
            {
                if (TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
                    netObj.Despawn(false);
                Destroy(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void Update()
        {
            if (!CanExecuteLogic) return;

            if (m_Health != null && m_Health.CurrentHP <= 0)
            {
                OnEnemyDeath();
                return;
            }

            UpdateHoverOffsetForCategory();
            DetectAndDodgeHazards();
            EvaluatePhaseTransitions();
            FindTarget();
            ExecuteArchetypeBehavior();

            float speed = m_Agent != null ? m_Agent.velocity.magnitude : 0;
            UpdateAnimator(speed, true);
        }

        private void SetPhase(int index)
        {
            if (phases == null || phases.Count == 0) return;

            currentPhaseIndex = Mathf.Clamp(index, 0, phases.Count - 1);
            EnemyPhase phase = phases[currentPhaseIndex];

            activePhaseName = phase.phaseName;
            activeArchetype = phase.behaviorArchetype;
            _phaseStartTime = Time.time;
            _damageTakenInPhase = 0;

            // Audio & VFX
            if (phase.phaseEnterVfx != null)
            {
                Instantiate(phase.phaseEnterVfx, transform.position + Vector3.up, Quaternion.identity);
            }
            if (phase.phaseEnterSound != null)
            {
                AudioSource.PlayClipAtPoint(phase.phaseEnterSound, transform.position);
            }

            ValidateRequiredControllers();

            //Debug.Log($"<color=orange>[EnemyAI]</color> {gameObject.name} entró a {activePhaseName} (Arquetipo: {activeArchetype})");
        }

        private void EvaluatePhaseTransitions()
        {
            if (phases == null || currentPhaseIndex >= phases.Count - 1) return;

            EnemyPhase currentPhase = phases[currentPhaseIndex];
            if (currentPhase.transitionTriggers == null || currentPhase.transitionTriggers.Count == 0) return;

            bool shouldTransition = false;

            if (currentPhase.triggerRequirement == TriggerRequirement.AnyTriggerMode)
            {
                foreach (var trigger in currentPhase.transitionTriggers)
                {
                    if (CheckTrigger(trigger)) { shouldTransition = true; break; }
                }
            }
            else
            {
                shouldTransition = true;
                foreach (var trigger in currentPhase.transitionTriggers)
                {
                    if (!CheckTrigger(trigger)) { shouldTransition = false; break; }
                }
            }

            if (shouldTransition)
            {
                SetPhase(currentPhaseIndex + 1);
            }
        }

        private bool CheckTrigger(PhaseTrigger trigger)
        {
            float hpPercent = m_Health != null ? ((float)m_Health.CurrentHP / Mathf.Max(1, m_Health.maxHealth)) * 100f : 100f;
            float distToTarget = m_Target != null ? Vector3.Distance(transform.position, m_Target.position) : 999f;

            switch (trigger.triggerType)
            {
                case PhaseTriggerType.HealthPercentageLessThan:
                    return hpPercent <= trigger.thresholdValue;

                case PhaseTriggerType.HealthAbsoluteLessThan:
                    return m_Health != null && m_Health.CurrentHP <= trigger.thresholdValue;

                case PhaseTriggerType.PlayerDistanceLessThan:
                    return distToTarget <= trigger.thresholdValue;

                case PhaseTriggerType.PlayerDistanceGreaterThan:
                    return distToTarget >= trigger.thresholdValue;

                case PhaseTriggerType.PlayerCountGreaterThan:
                    return CountNearbyPlayers(EffectiveDetectionRange) >= trigger.thresholdValue;

                case PhaseTriggerType.AlliesNearbyLessThan:
                    return CountNearbyAllies(EffectiveDetectionRange) <= trigger.thresholdValue;

                case PhaseTriggerType.TimeInPhaseGreaterThan:
                    return (Time.time - _phaseStartTime) >= trigger.thresholdValue;

                case PhaseTriggerType.DamageTakenInPhaseGreaterThan:
                    return _damageTakenInPhase >= trigger.thresholdValue;

                case PhaseTriggerType.LineOfSight:
                    return HasLineOfSightToTarget();

                default:
                    return false;
            }
        }

        private int CountNearbyPlayers(float radius)
        {
            int count = 0;
            var players = GameObject.FindGameObjectsWithTag(playerTag);
            foreach (var p in players)
            {
                if (Vector3.Distance(transform.position, p.transform.position) <= radius) count++;
            }
            return count;
        }

        private int CountNearbyAllies(float radius)
        {
            int count = 0;
            var enemies = GameObject.FindGameObjectsWithTag("Enemy");
            foreach (var e in enemies)
            {
                if (e != gameObject && Vector3.Distance(transform.position, e.transform.position) <= radius) count++;
            }
            return count;
        }

        private bool HasLineOfSightToTarget()
        {
            if (m_Target == null) return false;
            Vector3 origin = transform.position + Vector3.up * 1.5f;
            Vector3 dir = (m_Target.position + Vector3.up - origin).normalized;
            float dist = Vector3.Distance(origin, m_Target.position + Vector3.up);

            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist))
            {
                return hit.transform.root == m_Target.root;
            }
            return true;
        }

        private void UpdateHoverOffsetForCategory()
        {
            if (m_Agent == null) return;
            EnemyCategory cat = GetCurrentCategory();
            if (cat == EnemyCategory.Melee)
            {
                m_Agent.baseOffset = 0f;
            }
            else if (cat == EnemyCategory.Ranged)
            {
                m_Agent.baseOffset = EffectiveHoverHeight;
            }
            else
            {
                bool isMeleeClose = m_Target != null && Vector3.Distance(transform.position, m_Target.position) <= EffectiveMeleeRange * 1.5f;
                m_Agent.baseOffset = isMeleeClose ? 0f : EffectiveHoverHeight;
            }
        }

        private Vector3 FindCoverPosition(Vector3 targetPos)
        {
            Vector3 bestCover = transform.position;
            float bestDist = float.MaxValue;
            Vector3 origin = transform.position;

            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                Vector3 dir = Quaternion.Euler(0, angle, 0) * transform.forward;
                Vector3 samplePos = origin + dir * 12f;

                if (NavMesh.SamplePosition(samplePos, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                {
                    Vector3 eyePos = hit.position + Vector3.up * 1.5f;
                    Vector3 targetEye = targetPos + Vector3.up * 1.5f;
                    Vector3 rayDir = (targetEye - eyePos).normalized;

                    if (Physics.Raycast(eyePos, rayDir, Vector3.Distance(eyePos, targetEye), ~0, QueryTriggerInteraction.Ignore))
                    {
                        float d = Vector3.Distance(transform.position, hit.position);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestCover = hit.position;
                        }
                    }
                }
            }

            return bestCover;
        }

        private void FindTarget()
        {
            if (m_Target != null)
            {
                if (Vector3.Distance(transform.position, m_Target.position) > EffectiveDetectionRange * 1.5f)
                    m_Target = null;
            }

            if (m_Target == null)
            {
                var players = GameObject.FindGameObjectsWithTag(playerTag);
                float closest = EffectiveDetectionRange;
                foreach (var p in players)
                {
                    float d = Vector3.Distance(transform.position, p.transform.position);
                    if (d <= closest)
                    {
                        Vector3 dirToP = (p.transform.position - transform.position).normalized;
                        float angle = Vector3.Angle(transform.forward, dirToP);

                        bool inFOV = angle <= (EffectiveVisionAngle * 0.5f) && HasLineOfSightToTarget();
                        bool inProximity = d <= 6.0f;

                        if (inFOV || inProximity)
                        {
                            closest = d;
                            m_Target = p.transform;
                            currentState = AIState.Alerta;
                        }
                    }
                }
            }
        }

        private EnemyCategory GetCurrentCategory()
        {
            if (phases != null && currentPhaseIndex < phases.Count)
            {
                EnemyPhase phase = phases[currentPhaseIndex];
                if (phase.overrideCategory) return phase.categoryOverride;
            }
            return enemyCategory;
        }

        private float GetCurrentSpeedMultiplier()
        {
            if (phases != null && currentPhaseIndex < phases.Count)
                return phases[currentPhaseIndex].speedMultiplier;
            return 1.0f;
        }

        private void AnalyzeTargetAndReactTactically(ref AIArchetype currentArchetype)
        {
            if (m_Target == null) return;

            HealthController targetHealth = m_Target.GetComponent<HealthController>() ?? m_Target.GetComponentInParent<HealthController>();
            ShieldController targetShield = m_Target.GetComponent<ShieldController>() ?? m_Target.GetComponentInParent<ShieldController>();
            ShootController targetShooter = m_Target.GetComponent<ShootController>() ?? m_Target.GetComponentInParent<ShootController>();

            if (m_Health != null && (m_Health.CurrentHP / (float)m_Health.maxHealth) < 0.30f)
            {
                currentArchetype = AIArchetype.FlanqueoYCobertura;
                return;
            }

            if (targetShooter != null && (targetShooter.isReloading || targetShooter.currentAmmo == 0))
            {
                currentArchetype = AIArchetype.CargaFrenetica;
                return;
            }

            if (targetHealth != null && (targetHealth.CurrentHP / (float)targetHealth.maxHealth) < 0.25f)
            {
                currentArchetype = AIArchetype.CargaFrenetica;
                return;
            }

            if (targetShield != null && targetShield.IsShieldActive)
            {
                currentArchetype = AIArchetype.FlanqueoYCobertura;
                return;
            }
        }

        private void ExecuteArchetypeBehavior()
        {
            AIArchetype archetypeToRun = activeArchetype;

            if (archetypeToRun == AIArchetype.SecuenciaMultifase)
            {
                int seqIndex = currentPhaseIndex % 8;
                archetypeToRun = (AIArchetype)seqIndex;
            }

            AnalyzeTargetAndReactTactically(ref archetypeToRun);

            EnemyCategory category = GetCurrentCategory();
            float speedMult = GetCurrentSpeedMultiplier();

            switch (archetypeToRun)
            {
                case AIArchetype.CargaDirecta:
                    ComportamientoCargaDirecta(category, speedMult);
                    break;
                case AIArchetype.FlanqueoYCobertura:
                    ComportamientoFlanqueoYCobertura(category, speedMult);
                    break;
                case AIArchetype.CargaFrenetica:
                    ComportamientoCargaFrenetica(category, speedMult);
                    break;
                case AIArchetype.GuardiaConEscudo:
                    ComportamientoGuardiaConEscudo(category, speedMult);
                    break;
                case AIArchetype.AtaqueYHuida:
                    ComportamientoAtaqueYHuida(category, speedMult);
                    break;
                case AIArchetype.InvocadorRefuerzos:
                    ComportamientoInvocadorRefuerzos(category, speedMult);
                    break;
                case AIArchetype.EmboscadaEnSigilo:
                    ComportamientoEmboscadaEnSigilo(category, speedMult);
                    break;
                case AIArchetype.MovimientoErratico:
                    ComportamientoMovimientoErratico(category, speedMult);
                    break;
                default:
                    ComportamientoCargaDirecta(category, speedMult);
                    break;
            }
        }

        // --- Archetype Implementations (Exaggerated & Category-Agnostic) ---

        private void ComportamientoCargaDirecta(EnemyCategory category, float speedMult)
        {
            if (m_Target != null)
            {
                float dist = Vector3.Distance(transform.position, m_Target.position);
                currentState = AIState.Persecucion;

                // Carga frontal directa acelerada e implacable
                MoveTo(m_Target.position, EffectiveChaseSpeed * 2.8f * speedMult);
                RotateBaseTowards(m_Target.position);

                bool isMeleeRange = dist <= EffectiveMeleeRange * 1.3f;
                bool isShootRange = dist <= EffectiveShootRange;

                if (ShouldAttack(category, isMeleeRange, isShootRange))
                {
                    currentState = AIState.Ataque;
                    ExecuteCombatAction(category, isMeleeRange, isShootRange);
                }
            }
            else
            {
                currentState = AIState.Patrulla;
                Wander(speedMult);
            }
        }

        private void ComportamientoFlanqueoYCobertura(EnemyCategory category, float speedMult)
        {
            if (m_Target != null)
            {
                float dist = Vector3.Distance(transform.position, m_Target.position);
                RotateBaseTowards(m_Target.position);

                float idealMin = EffectiveShootRange * 0.45f;
                float idealMax = EffectiveShootRange * 0.85f;

                if (dist < idealMin)
                {
                    Vector3 coverPos = FindCoverPosition(m_Target.position);
                    MoveTo(coverPos, EffectiveChaseSpeed * 2.5f * speedMult);
                    currentState = AIState.Huida;
                }
                else if (dist > idealMax)
                {
                    MoveTo(m_Target.position, EffectiveChaseSpeed * 1.8f * speedMult);
                    currentState = AIState.Persecucion;
                }
                else
                {
                    Vector3 coverPos = FindCoverPosition(m_Target.position);
                    MoveTo(coverPos, EffectiveChaseSpeed * 2.2f * speedMult);
                    currentState = AIState.Ataque;
                }

                ExecuteCombatAction(category, dist <= EffectiveMeleeRange * 1.2f, dist <= EffectiveShootRange);
            }
            else
            {
                currentState = AIState.Patrulla;
                Wander(speedMult);
            }
        }

        private void ComportamientoCargaFrenetica(EnemyCategory category, float speedMult)
        {
            if (m_Target != null)
            {
                float dist = Vector3.Distance(transform.position, m_Target.position);
                currentState = AIState.Persecucion;

                // Embestida descontrolada imparable a 3.8x de velocidad
                MoveTo(m_Target.position, EffectiveChaseSpeed * 3.8f * speedMult);
                RotateBaseTowards(m_Target.position);

                bool isMeleeRange = dist <= EffectiveMeleeRange * 1.6f;
                bool isShootRange = dist <= EffectiveShootRange;

                if (ShouldAttack(category, isMeleeRange, isShootRange))
                {
                    currentState = AIState.Ataque;
                    ExecuteCombatAction(category, isMeleeRange, isShootRange);
                }
            }
            else
            {
                currentState = AIState.Patrulla;
                Wander(speedMult * 2.0f);
            }
        }

        private void ComportamientoGuardiaConEscudo(EnemyCategory category, float speedMult)
        {
            var shield = GetComponent<ShieldController>() ?? GetComponentInParent<ShieldController>();
            if (shield != null && !shield.IsShieldActive)
            {
                shield.SetShieldState(true);
            }

            float distFromAnchor = Vector3.Distance(transform.position, _startPosition);

            if (m_Target != null && Vector3.Distance(transform.position, m_Target.position) <= EffectiveDetectionRange)
            {
                float distToTarget = Vector3.Distance(transform.position, m_Target.position);
                RotateBaseTowards(m_Target.position);

                if (distFromAnchor > EffectiveWanderRadius)
                {
                    // Retorno firme al puesto de guardia
                    MoveTo(_startPosition, EffectiveChaseSpeed * 1.5f * speedMult);
                    currentState = AIState.Guardia;
                }
                else
                {
                    currentState = AIState.Ataque;
                    ExecuteCombatAction(category, distToTarget <= EffectiveMeleeRange * 1.2f, distToTarget <= EffectiveShootRange);
                    MoveTo(m_Target.position, EffectiveWanderSpeed * 0.5f * speedMult);
                }
            }
            else
            {
                currentState = AIState.Guardia;
                if (distFromAnchor > 1.5f) MoveTo(_startPosition, EffectiveWanderSpeed * 0.8f * speedMult);
                else StopMoving();
            }
        }

        private void ComportamientoAtaqueYHuida(EnemyCategory category, float speedMult)
        {
            if (m_Target != null)
            {
                float dist = Vector3.Distance(transform.position, m_Target.position);
                RotateBaseTowards(m_Target.position);

                float safeDist = EffectiveShootRange * 0.85f;

                if (dist < safeDist)
                {
                    Vector3 coverPos = FindCoverPosition(m_Target.position);
                    MoveTo(coverPos, EffectiveChaseSpeed * 3.2f * speedMult);
                    currentState = AIState.Huida;
                }
                else
                {
                    currentState = AIState.Ataque;
                    StopMoving();
                }

                ExecuteCombatAction(category, dist <= EffectiveMeleeRange, dist <= EffectiveShootRange);
            }
            else
            {
                currentState = AIState.Patrulla;
                Wander(speedMult);
            }
        }

        private void ComportamientoInvocadorRefuerzos(EnemyCategory category, float speedMult)
        {
            if (m_Spawn == null)
            {
                Debug.LogError($"<color=red>[EnemyController Error]</color> El arquetipo INVOCADOR REFUERZOS en '{gameObject.name}' REQUIERE un componente SpawnController asignado.");
            }

            if (m_Target != null)
            {
                float dist = Vector3.Distance(transform.position, m_Target.position);

                // Posicionamiento en retaguardia
                Vector3 awayFromTarget = transform.position + (transform.position - m_Target.position).normalized * 15f;
                MoveTo(awayFromTarget, EffectiveChaseSpeed * 1.4f * speedMult);
                RotateBaseTowards(m_Target.position);

                // Invocación controlada para proteger la CPU: Máximo 4 aliados cercanos y cooldown de 10s
                int nearbyAllies = CountNearbyAllies(35f);
                const int MAX_MINIONS_LIMIT = 4;

                if (nearbyAllies < MAX_MINIONS_LIMIT && Time.time > _supportSummonTimer)
                {
                    _supportSummonTimer = Time.time + 10.0f;
                    SpawnReinforcementMinion();
                }

                currentState = AIState.Ataque;
                ExecuteCombatAction(category, dist <= EffectiveMeleeRange, dist <= EffectiveShootRange);
            }
            else
            {
                currentState = AIState.Patrulla;
                Wander(speedMult);
            }
        }

        private void SpawnReinforcementMinion()
        {
            if (m_Spawn == null) return;

            GameObject prefabToSpawn = null;

            if (m_Spawn.itemsToSpawn != null && m_Spawn.itemsToSpawn.Count > 0)
            {
                var valid = m_Spawn.itemsToSpawn.Where(i => i.prefab != null && i.prefab != gameObject).ToList();
                if (valid.Count > 0) prefabToSpawn = valid[Random.Range(0, valid.Count)].prefab;
            }

            if (prefabToSpawn == null && m_Spawn.lootTable != null && m_Spawn.lootTable.Count > 0)
            {
                var validLoot = m_Spawn.lootTable.Where(l => l != null && l.itemPrefab != null && l.itemPrefab != gameObject).ToList();
                if (validLoot.Count > 0) prefabToSpawn = validLoot[Random.Range(0, validLoot.Count)].itemPrefab;
            }

            if (prefabToSpawn == null) prefabToSpawn = gameObject;

            Vector3 spawnPos = transform.position + transform.right * 2.5f + Vector3.up * 0.5f;
            GameObject spawnedMinion = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);

            var minionAI = spawnedMinion.GetComponent<EnemyController>();
            if (minionAI != null)
            {
                minionAI.mainArchetype = AIArchetype.CargaDirecta;
                minionAI.activeArchetype = AIArchetype.CargaDirecta;
            }

            if (IsNetworkActive && IsServer)
            {
                var netObj = spawnedMinion.GetComponent<NetworkObject>();
                if (netObj != null && !netObj.IsSpawned) netObj.Spawn();
            }
        }

        private void ComportamientoEmboscadaEnSigilo(EnemyCategory category, float speedMult)
        {
            if (m_Target != null)
            {
                float dist = Vector3.Distance(transform.position, m_Target.position);

                if (!_isStealthActive && dist > EffectiveMeleeRange * 2.5f)
                {
                    // Modo Sigilo: Inmóvil total aguardando presa
                    currentState = AIState.Sigilo;
                    StopMoving();
                    RotateBaseTowards(m_Target.position);

                    if (dist <= EffectiveDetectionRange * 0.45f)
                    {
                        _isStealthActive = true;
                    }
                }
                else
                {
                    // Emboscada explosiva a 4.0x de velocidad
                    currentState = AIState.Persecucion;
                    MoveTo(m_Target.position, EffectiveChaseSpeed * 4.0f * speedMult);
                    RotateBaseTowards(m_Target.position);

                    bool isMeleeRange = dist <= EffectiveMeleeRange * 1.6f;
                    bool isShootRange = dist <= EffectiveShootRange;

                    if (ShouldAttack(category, isMeleeRange, isShootRange))
                    {
                        currentState = AIState.Ataque;
                        ExecuteCombatAction(category, isMeleeRange, isShootRange);
                    }
                }
            }
            else
            {
                _isStealthActive = false;
                currentState = AIState.Patrulla;
                Wander(speedMult * 0.4f);
            }
        }

        private void ComportamientoMovimientoErratico(EnemyCategory category, float speedMult)
        {
            if (m_Target != null)
            {
                float dist = Vector3.Distance(transform.position, m_Target.position);

                // Cambios erráticos e hiper-rápidos en zigzag (intervalos de 0.25s - 0.45s)
                if (Time.time > _chaosTimer)
                {
                    _chaosTimer = Time.time + Random.Range(0.25f, 0.45f);
                    Vector3 randSide = Vector3.Cross(Vector3.up, (m_Target.position - transform.position).normalized);
                    float sideSign = (Random.value > 0.5f) ? 1f : -1f;
                    _chaosDirection = (randSide * sideSign * 10f) + (transform.forward * Random.Range(-4f, 8f));
                    _chaosDirection.y = 0;
                }

                Vector3 targetMovePos = transform.position + _chaosDirection;
                MoveTo(targetMovePos, EffectiveChaseSpeed * 3.2f * speedMult);
                RotateBaseTowards(m_Target.position);

                currentState = AIState.Ataque;
                ExecuteCombatAction(category, dist <= EffectiveMeleeRange * 1.2f, dist <= EffectiveShootRange);
            }
            else
            {
                currentState = AIState.Patrulla;
                Wander(speedMult * 1.8f);
            }
        }

        // --- Helper Methods ---

        private static HashSet<EnemyController> s_ActiveMeleeAttackers = new HashSet<EnemyController>();
        private const int MAX_CONCURRENT_MELEE_ATTACKERS = 2;

        private bool RequestMeleeAttackToken()
        {
            s_ActiveMeleeAttackers.RemoveWhere(e => e == null || !e.enabled || e.m_Health == null || e.m_Health.CurrentHP <= 0);
            if (s_ActiveMeleeAttackers.Contains(this)) return true;
            if (s_ActiveMeleeAttackers.Count < MAX_CONCURRENT_MELEE_ATTACKERS)
            {
                s_ActiveMeleeAttackers.Add(this);
                return true;
            }
            return false;
        }

        private void ReleaseMeleeAttackToken()
        {
            s_ActiveMeleeAttackers.Remove(this);
        }

        private Vector3 GetPredictedTargetPosition()
        {
            if (m_Target == null) return transform.position + transform.forward * 10f;
            Vector3 targetPos = m_Target.position + Vector3.up;

            Vector3 targetVel = Vector3.zero;
            if (m_Target.TryGetComponent<CharacterController>(out var cc)) targetVel = cc.velocity;
            else if (m_Target.TryGetComponent<Rigidbody>(out var rb)) targetVel = rb.linearVelocity;

            if (m_Shooter != null && m_Shooter.Projectile != null)
            {
                var proj = m_Shooter.Projectile.GetComponent<ProjectileController>();
                float speed = proj != null ? proj.EffectiveSpeed : 30f;
                float dist = Vector3.Distance(transform.position, m_Target.position);
                float travelTime = dist / Mathf.Max(1f, speed);
                targetPos += targetVel * travelTime;
            }

            return targetPos;
        }

        private void DetectAndDodgeHazards()
        {
            Collider[] hazards = Physics.OverlapSphere(transform.position, 6.0f);
            foreach (var c in hazards)
            {
                if (c == null) continue;
                var proj = c.GetComponent<ProjectileController>();
                if (proj != null && proj.m_OwnerTeam != Team.Enemy)
                {
                    Vector3 dodgeDir = Vector3.Cross(Vector3.up, (proj.transform.position - transform.position).normalized);
                    MoveTo(transform.position + dodgeDir * 7f, EffectiveChaseSpeed * 3.2f);
                    return;
                }
            }
        }

        private bool ShouldAttack(EnemyCategory category, bool isMeleeRange, bool isShootRange)
        {
            bool inMaxShootRange = m_Target != null && Vector3.Distance(transform.position, m_Target.position) <= EffectiveMaxProjectileReach;
            switch (category)
            {
                case EnemyCategory.Melee:
                    return isMeleeRange && m_Melee != null && RequestMeleeAttackToken();
                case EnemyCategory.Ranged:
                    return inMaxShootRange && m_Shooter != null;
                case EnemyCategory.Hybrid:
                    return (isMeleeRange && m_Melee != null && RequestMeleeAttackToken()) || (inMaxShootRange && m_Shooter != null);
                default:
                    return isMeleeRange || inMaxShootRange;
            }
        }

        private void ExecuteCombatAction(EnemyCategory category, bool isMeleeRange, bool isShootRange)
        {
            if (m_Target == null) return;
            Vector3 aimPos = GetPredictedTargetPosition();
            RotateBaseTowards(aimPos);

            switch (category)
            {
                case EnemyCategory.Melee:
                    if (m_Melee != null && isMeleeRange && RequestMeleeAttackToken())
                    {
                        m_Melee.PerformMeleeAction(m_Target.position);
                    }
                    else
                    {
                        ReleaseMeleeAttackToken();
                    }
                    break;

                case EnemyCategory.Ranged:
                    if (m_Shooter != null && isShootRange)
                    {
                        m_Shooter.FireAt(aimPos);
                    }
                    break;

                case EnemyCategory.Hybrid:
                    if (isMeleeRange && m_Melee != null && RequestMeleeAttackToken())
                    {
                        m_Melee.PerformMeleeAction(m_Target.position);
                    }
                    else if (isShootRange && m_Shooter != null)
                    {
                        ReleaseMeleeAttackToken();
                        m_Shooter.FireAt(aimPos);
                    }
                    break;
            }
        }

        private void Wander(float speedMult = 1.0f)
        {
            if (m_Agent == null || !m_Agent.isOnNavMesh || m_Agent.pathPending || m_Agent.remainingDistance > 1f) return;

            Vector3 randomPos = _startPosition + Random.insideUnitSphere * EffectiveWanderRadius;
            if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, EffectiveWanderRadius, 1))
            {
                MoveTo(hit.position, EffectiveWanderSpeed * speedMult);
            }

            if (m_Agent.velocity.sqrMagnitude > 0.1f)
            {
                RotateBaseTowards(transform.position + m_Agent.velocity);
            }
        }

        private void MoveTo(Vector3 position, float speed)
        {
            if (m_Agent != null && m_Agent.isOnNavMesh)
            {
                m_Agent.speed = speed;
                m_Agent.isStopped = false;
                m_Agent.SetDestination(position);
            }
        }

        private void StopMoving()
        {
            if (m_Agent != null && m_Agent.isOnNavMesh) m_Agent.isStopped = true;
        }

        private void RotateBaseTowards(Vector3 position)
        {
            Vector3 direction = (position - transform.position);
            if (GetCurrentCategory() == EnemyCategory.Melee) direction.y = 0;

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetYaw = Quaternion.LookRotation(direction.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetYaw, EffectiveTurnSpeed * Time.deltaTime);
            }
        }

        private void UpdateAnimator(float speed, bool grounded)
        {
            if (m_Animator == null || !m_Animator.gameObject.activeInHierarchy)
            {
                m_Animator = GetComponentInChildren<Animator>(true);
                if (m_Animator != null)
                {
                    m_Animator.enabled = true;
                    _hasAnimSpeed = HasParameter(m_Animator, _animIDSpeed);
                    _hasAnimGrounded = HasParameter(m_Animator, _animIDIsGrounded);
                }
            }

            if (m_Animator == null) return;
            if (!m_Animator.enabled) m_Animator.enabled = true;

            float speedParam = 0f;
            if (speed > 0.01f)
            {
                if (currentState == AIState.Persecucion || currentState == AIState.Huida)
                {
                    speedParam = Mathf.Lerp(0.5f, 1.0f, speed / Mathf.Max(0.1f, EffectiveChaseSpeed));
                }
                else
                {
                    speedParam = Mathf.Lerp(0.1f, 0.5f, speed / Mathf.Max(0.1f, EffectiveWanderSpeed));
                }
            }
            speedParam = Mathf.Clamp01(speedParam);

            bool isTerrestrial = GetCurrentCategory() == EnemyCategory.Melee;
            if (_hasAnimSpeed) m_Animator.SetFloat(_animIDSpeed, speedParam);
            if (_hasAnimGrounded) m_Animator.SetBool(_animIDIsGrounded, isTerrestrial);
        }

        public override void OnDestroy()
        {
            if (m_Health != null)
            {
                m_Health.OnTakeDamage.RemoveListener(OnDamageTaken);
                m_Health.OnDeath.RemoveListener(OnEnemyDeath);
            }
            base.OnDestroy();
        }
    }
}
