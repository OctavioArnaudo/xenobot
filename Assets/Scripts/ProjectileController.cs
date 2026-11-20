using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using Runtime.Scripts.Health;
using Runtime.Scripts.Enemy;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Player;
using Runtime.Scripts.Shoot;
using Runtime.Scripts.Crystals;
using Runtime.Scripts.Audio;

namespace Runtime.Scripts.Projectile
{
    public enum ProjectileType
    {
        BalaFuego,
        RayoContinuo,
        BalaCongelante,
        BalaCorrosiva,
        BalaExplosiva
    }

    public class ProjectileStatusReceiver : NetworkBehaviour
    {
        private int m_FreezeStacks = 0;
        private float m_FreezeTimer = 0f;
        private float m_SlowPerStack = 0.25f;

        private int m_CorrosionStacks = 0;
        private float m_CorrosionTimer = 0f;
        private float m_CorrosionDamagePerStack = 8f;
        private float m_CorrosionTickTimer = 0f;

        private float m_BaseNavSpeed = -1f;
        private float m_BaseMoveSpeed = -1f;
        private float m_BaseSprintSpeed = -1f;

        private HealthController m_Health;
        private EnemyController m_Enemy;

        void Awake()
        {
            m_Health = GetComponent<HealthController>() ?? GetComponentInParent<HealthController>();
            m_Enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
        }

        public void ApplyFreeze(float slowPerStack, float duration)
        {
            m_FreezeStacks++;
            m_SlowPerStack = slowPerStack;
            m_FreezeTimer = duration;
            UpdateSlowEffect();
        }

        public void ApplyCorrosion(float damagePerStack, float duration)
        {
            m_CorrosionStacks++;
            m_CorrosionDamagePerStack = damagePerStack;
            m_CorrosionTimer = duration;
        }

        void Update()
        {
            if (m_FreezeTimer > 0f)
            {
                m_FreezeTimer -= Time.deltaTime;
                if (m_FreezeTimer <= 0f)
                {
                    m_FreezeStacks = 0;
                    UpdateSlowEffect();
                }
            }

            if (m_CorrosionTimer > 0f)
            {
                m_CorrosionTimer -= Time.deltaTime;
                m_CorrosionTickTimer += Time.deltaTime;

                if (m_CorrosionTickTimer >= 0.5f)
                {
                    m_CorrosionTickTimer = 0f;
                    if (m_Health != null && m_Health.EffectiveCurrentHealth > 0)
                    {
                        int dotDamage = Mathf.Max(1, Mathf.RoundToInt(m_CorrosionStacks * m_CorrosionDamagePerStack * 0.5f));
                        m_Health.TakeDamage(dotDamage);
                    }
                }

                if (m_CorrosionTimer <= 0f)
                {
                    m_CorrosionStacks = 0;
                }
            }
        }

        private void UpdateSlowEffect()
        {
            float speedMultiplier = Mathf.Clamp(1.0f - (m_FreezeStacks * m_SlowPerStack), 0.1f, 1.0f);

            var navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>() ?? GetComponentInParent<UnityEngine.AI.NavMeshAgent>();
            if (navAgent != null)
            {
                if (m_BaseNavSpeed <= 0f) m_BaseNavSpeed = navAgent.speed;
                navAgent.speed = m_BaseNavSpeed * speedMultiplier;
            }

            var player = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
            if (player != null)
            {
                if (m_BaseMoveSpeed <= 0f) m_BaseMoveSpeed = player.MoveSpeed;
                if (m_BaseSprintSpeed <= 0f) m_BaseSprintSpeed = player.SprintSpeed;

                player.MoveSpeed = m_BaseMoveSpeed * speedMultiplier;
                player.SprintSpeed = m_BaseSprintSpeed * speedMultiplier;
            }
        }
    }

    /// <summary>
    /// Pool de balas en red: el manejador de Netcode crea y "destruye" las balas en TODOS los peers (servidor y clientes)
    /// reutilizando instancias, asi nadie hace Instantiate/Destroy por disparo.
    /// </summary>
    public static class ProjectilePool
    {
        private sealed class Handler : INetworkPrefabInstanceHandler
        {
            private readonly GameObject m_Prefab;
            private readonly Stack<GameObject> m_Free = new Stack<GameObject>();

            public Handler(GameObject prefab) { m_Prefab = prefab; }

            public GameObject Take(Vector3 pos, Quaternion rot)
            {
                GameObject g = null;
                while (m_Free.Count > 0 && (g = m_Free.Pop()) == null) { }
                if (g == null) g = Object.Instantiate(m_Prefab);
                g.transform.SetPositionAndRotation(pos, rot);
                g.SetActive(true);
                if (g.TryGetComponent(out ProjectileController p)) p.PrepareForSpawn(pos, rot);
                return g;
            }

            public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
                => Take(position, rotation).GetComponent<NetworkObject>();

            public void Destroy(NetworkObject networkObject)
            {
                networkObject.gameObject.SetActive(false);
                m_Free.Push(networkObject.gameObject);
            }
        }

        private static readonly Dictionary<GameObject, Handler> s_Handlers = new Dictionary<GameObject, Handler>();
        private static NetworkManager s_Manager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_Handlers.Clear(); s_Manager = null; }

        /// <summary>Registra un manejador por cada prefab de red que sea una bala. Idempotente; llamar en cada peer.</summary>
        public static void RegisterAll()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;
            if (s_Manager != nm) { s_Manager = nm; s_Handlers.Clear(); }   // sesion de red nueva: registrar de nuevo

            foreach (var np in nm.NetworkConfig.Prefabs.Prefabs)
            {
                GameObject prefab = np.Prefab;
                if (prefab == null || prefab.GetComponent<ProjectileController>() == null || s_Handlers.ContainsKey(prefab)) continue;

                var h = new Handler(prefab);
                s_Handlers[prefab] = h;
                nm.PrefabHandler.AddHandler(prefab, h);
            }
        }

        /// <summary>Solo servidor: bala lista para Launch() y luego NetworkObject.Spawn().</summary>
        public static GameObject Get(GameObject prefab, Vector3 pos, Quaternion rot)
            => s_Handlers.TryGetValue(prefab, out var h) ? h.Take(pos, rot) : Object.Instantiate(prefab, pos, rot);
    }

    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class ProjectileController : NetworkBehaviour, IPoolRecyclable
    {
        public event System.Action OnEntityRecycled;
        private static readonly List<ProjectileController> s_ActiveProjectiles = new List<ProjectileController>();
        private static readonly RaycastHit[] s_Hits = new RaycastHit[16];
        private sealed class ByDistance : IComparer<RaycastHit> { public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance); }
        private static readonly ByDistance s_ByDistance = new ByDistance();
        private static readonly Dictionary<Color, Material> s_FallbackMats = new Dictionary<Color, Material>();
        private const int MAX_SIMULTANEOUS_PROJECTILES = 40;

        private const ProjectileType DEFAULT_TYPE = ProjectileType.BalaFuego;
        private const bool DEFAULT_AUTO_RANDOMIZE = false;
        private const float DEFAULT_SPEED = 45f;
        private const float DEFAULT_DAMAGE = 25f;
        private const float DEFAULT_LIFETIME = 3.5f;
        private const float DEFAULT_HOMING_TURN_SPEED = 30f;
        private const float DEFAULT_DETECTION_RADIUS = 40f;
        private const float DEFAULT_EXPLOSION_RADIUS = 4f;
        private const float DEFAULT_FREEZE_SLOW_AMOUNT = 0.25f;
        private const float DEFAULT_CORROSION_DAMAGE = 8f;
        private const float DEFAULT_DOT_DURATION = 3.5f;

        [Header("Tipo de Proyectil")]
        public ProjectileType Type;
        public Optional<ProjectileType> type;
        public bool AutoRandomize;
        public Optional<bool> autoRandomize;

        [Header("Estadísticas Generales")]
        public Color Color;
        public Optional<Color> color;
        public float Damage;
        public Optional<float> damage;
        public float DetectionRadius;
        public Optional<float> detectionRadius;
        public float HomingTurnSpeed;
        public Optional<float> homingTurnSpeed;
        public float LifeTime;
        public Optional<float> lifeTime;
        public float Speed;
        public Optional<float> speed;

        [Header("Homing / Tracking")]
        public bool enableHomingTracking = false;

        [Header("Efectos Especiales")]
        public float CorrosionDamage;
        public Optional<float> corrosionDamage;
        public float DotDuration;
        public Optional<float> dotDuration;
        public float ExplosionRadius;
        public Optional<float> explosionRadius;
        public float FreezeSlowAmount;
        public Optional<float> freezeSlowAmount;

        [Header("Visuales y Efectos de Impacto")]
        public GameObject visualsBalaFuego;
        public GameObject visualsRayoContinuo;
        public GameObject visualsBalaCongelante;
        public GameObject visualsBalaCorrosiva;
        public GameObject visualsBalaExplosiva;
        public ParticleSystem explosionVFX;
        public ParticleSystem impactVFX;

        private Vector3 m_Direction;
        private GameObject m_Owner;
        public Team m_OwnerTeam;
        private bool m_HasHit = false;
        private bool m_DamageFromLaunch;
        private Transform m_HomingTarget;
        private float m_LifeTimer = 0f;

        private bool m_IsAttached = false;
        private Transform m_AttachedTarget;
        private Vector3 m_AttachedLocalPos;

        private static List<ProjectileController> s_ActiveAttachedBombs = new List<ProjectileController>();
        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        public ProjectileType EffectiveType => type.GetValue(DEFAULT_TYPE);
        public bool EffectiveAutoRandomize => autoRandomize.GetValue(DEFAULT_AUTO_RANDOMIZE);
        private const float SPEED_MULTIPLIER = 2f;
        public float EffectiveSpeed => SPEED_MULTIPLIER * speed.GetValue(EffectiveType == ProjectileType.RayoContinuo ? DEFAULT_SPEED * 2.2f : DEFAULT_SPEED);
        public float EffectiveDamage
        {
            get
            {
                if (damage.use) return damage.value;

                if (m_Owner != null)
                {
                    var shooter = m_Owner.GetComponent<ShootController>() ?? m_Owner.GetComponentInChildren<ShootController>();
                    if (shooter != null) return shooter.EffectiveDamage;

                    var balance = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(m_Owner) : default;
                    if (balance.attackDamage.use) return balance.attackDamage.value;
                }

                var selfBalance = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                if (selfBalance.attackDamage.use) return (float)selfBalance.attackDamage.value;

                return Damage > 0f ? Damage : DEFAULT_DAMAGE;
            }
        }
        public float EffectiveLifeTime => lifeTime.GetValue(DEFAULT_LIFETIME);
        public float EffectiveHomingTurnSpeed => homingTurnSpeed.GetValue(DEFAULT_HOMING_TURN_SPEED);
        public float EffectiveDetectionRadius => detectionRadius.GetValue(DEFAULT_DETECTION_RADIUS);
        public float EffectiveExplosionRadius => explosionRadius.GetValue(DEFAULT_EXPLOSION_RADIUS);
        public float EffectiveFreezeSlowAmount => freezeSlowAmount.GetValue(DEFAULT_FREEZE_SLOW_AMOUNT);
        public float EffectiveCorrosionDamage => corrosionDamage.GetValue(DEFAULT_CORROSION_DAMAGE);
        public float EffectiveDotDuration => dotDuration.GetValue(DEFAULT_DOT_DURATION);
        public Color EffectiveColor => color.GetValue(GetDefaultColorForType(EffectiveType));

        private Color GetDefaultColorForType(ProjectileType type)
        {
            switch (type)
            {
                case ProjectileType.BalaFuego: return new Color(1.0f, 0.4f, 0.0f);
                case ProjectileType.RayoContinuo: return new Color(0.0f, 0.9f, 1.0f);
                case ProjectileType.BalaCongelante: return new Color(0.2f, 0.7f, 1.0f);
                case ProjectileType.BalaCorrosiva: return new Color(0.2f, 0.9f, 0.1f);
                case ProjectileType.BalaExplosiva: return new Color(1.0f, 0.85f, 0.0f);
                default: return Color.red;
            }
        }

        void Awake()
        {
            SetupPhysics();

            if (EffectiveAutoRandomize)
            {
                ProjectileType randomType = (ProjectileType)Random.Range(0, 5);
                type.use = true;
                type.value = randomType;
            }
        }

        private void Start()
        {
            if (visualsBalaFuego == null) visualsBalaFuego = transform.Find("Visuals_BalaFuego")?.gameObject;
            if (visualsRayoContinuo == null) visualsRayoContinuo = transform.Find("Visuals_RayoContinuo")?.gameObject;
            if (visualsBalaCongelante == null) visualsBalaCongelante = transform.Find("Visuals_BalaCongelante")?.gameObject;
            if (visualsBalaCorrosiva == null) visualsBalaCorrosiva = transform.Find("Visuals_BalaCorrosiva")?.gameObject;
            if (visualsBalaExplosiva == null) visualsBalaExplosiva = transform.Find("Visuals_BalaExplosiva")?.gameObject;

            RefreshVisuals();
        }

        private void SetupPhysics()
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            SphereCollider col = GetComponent<SphereCollider>();
            if (col == null) col = gameObject.AddComponent<SphereCollider>();

            if (col != null)
            {
                col.isTrigger = true;
                col.radius = 0.5f;
            }
        }

        public void PrepareForSpawn(Vector3 position, Quaternion rotation)
        {
            m_LifeTimer = 0f;
            m_HasHit = false;
            m_IsAttached = false;
            m_AttachedTarget = null;
            m_HomingTarget = null;
            enableHomingTracking = false;

            var col = GetComponent<Collider>();
            if (col != null) col.enabled = true;

            TrailRenderer[] trails = GetComponentsInChildren<TrailRenderer>();
            foreach (var tr in trails) tr.Clear();

            transform.SetPositionAndRotation(position, rotation);
        }

        public void RecycleEntity()
        {
            s_ActiveAttachedBombs.Remove(this);
            s_ActiveProjectiles.Remove(this);
            OnEntityRecycled?.Invoke();
        }

        public static void DetonateOwnerAttachedBombs(GameObject owner)
        {
            if (owner == null) return;
            var bombsToDetonate = s_ActiveAttachedBombs.FindAll(b => b != null && b.m_Owner == owner && b.m_IsAttached);
            foreach (var bomb in bombsToDetonate)
            {
                bomb.DetonateStickyBomb();
            }
        }

        public void Launch(GameObject owner, Vector3 direction, float dmg, Team team)
        {
            if (s_ActiveProjectiles.Count >= MAX_SIMULTANEOUS_PROJECTILES && s_ActiveProjectiles[0] != null)
            {
                s_ActiveProjectiles[0].FinalizeImpact();
            }

            if (!s_ActiveProjectiles.Contains(this))
            {
                s_ActiveProjectiles.Add(this);
            }

            m_LifeTimer = 0f;
            m_HasHit = false;
            m_IsAttached = false;
            m_AttachedTarget = null;
            m_HomingTarget = null;

            m_Owner = owner;
            m_Direction = direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
            m_OwnerTeam = team;

            DetonateOwnerAttachedBombs(owner);

            if (dmg > 0f && (!damage.use || m_DamageFromLaunch))
            {
                damage.use = true;
                damage.value = dmg;
                m_DamageFromLaunch = true;
            }

            if (m_Owner != null)
            {
                Collider[] ownerCols = m_Owner.GetComponentsInChildren<Collider>();
                Collider myCol = GetComponent<Collider>();
                if (myCol != null)
                {
                    foreach (var oc in ownerCols) Physics.IgnoreCollision(myCol, oc);
                }
            }

            RefreshVisuals();
            if (!IsNetworkActive) PlayShotSound();

            if (IsNetworkActive && IsServer)
            {
                var ownerNetObj = m_Owner != null ? (m_Owner.GetComponent<NetworkObject>() ?? m_Owner.GetComponentInParent<NetworkObject>() ?? m_Owner.GetComponentInChildren<NetworkObject>()) : null;
                ulong ownerId = ownerNetObj != null ? ownerNetObj.NetworkObjectId : 0;
                SyncLaunchRpc(ownerId, m_Direction, EffectiveDamage, m_OwnerTeam, enableHomingTracking);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SyncLaunchRpc(ulong ownerNetworkObjectId, Vector3 direction, float dmg, Team team, bool homing)
        {
            m_Direction = direction;
            m_OwnerTeam = team;
            enableHomingTracking = homing;
            if (dmg > 0f)
            {
                damage.use = true;
                damage.value = dmg;
                m_DamageFromLaunch = true;
            }

            if (ownerNetworkObjectId != 0 && NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(ownerNetworkObjectId, out var ownerNetObj))
            {
                m_Owner = ownerNetObj.gameObject;
                if (m_Owner != null)
                {
                    Collider[] ownerCols = m_Owner.GetComponentsInChildren<Collider>();
                    Collider myCol = GetComponent<Collider>();
                    if (myCol != null)
                    {
                        foreach (var oc in ownerCols) Physics.IgnoreCollision(myCol, oc);
                    }
                }
            }

            RefreshVisuals();
            PlayShotSound();
        }

        private void PlayShotSound() => AudioManager.Play(m_OwnerTeam == Team.Enemy ? Sfx.EnemyShoot : Sfx.Shoot, transform.position);

        private void RefreshVisuals()
        {
            ProjectileType type = EffectiveType;

            if (visualsBalaFuego != null) visualsBalaFuego.SetActive(type == ProjectileType.BalaFuego);
            if (visualsRayoContinuo != null) visualsRayoContinuo.SetActive(type == ProjectileType.RayoContinuo);
            if (visualsBalaCongelante != null) visualsBalaCongelante.SetActive(type == ProjectileType.BalaCongelante);
            if (visualsBalaCorrosiva != null) visualsBalaCorrosiva.SetActive(type == ProjectileType.BalaCorrosiva);
            if (visualsBalaExplosiva != null) visualsBalaExplosiva.SetActive(type == ProjectileType.BalaExplosiva);

            if (visualsBalaFuego == null && visualsRayoContinuo == null && visualsBalaCongelante == null &&
                visualsBalaCorrosiva == null && visualsBalaExplosiva == null)
            {
                GenerateFallbackVisuals();
            }
        }

        private void GenerateFallbackVisuals()
        {
            if (transform.Find("FallbackCore")) return;

            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "FallbackCore";
            core.transform.SetParent(transform, false);

            ProjectileType type = EffectiveType;
            if (type == ProjectileType.RayoContinuo)
                core.transform.localScale = new Vector3(0.12f, 0.12f, 1.8f);
            else if (type == ProjectileType.BalaExplosiva)
                core.transform.localScale = Vector3.one * 0.45f;
            else
                core.transform.localScale = Vector3.one * 0.35f;

            var c = core.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);

            Color col = EffectiveColor;
            if (!s_FallbackMats.TryGetValue(col, out Material mat))
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                mat = new Material(shader);
                mat.color = col;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                s_FallbackMats[col] = mat;
            }

            var mr = core.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            TrailRenderer trail = core.AddComponent<TrailRenderer>();
            if (trail != null)
            {
                trail.time = (type == ProjectileType.RayoContinuo) ? 0.4f : 0.25f;
                trail.startWidth = (type == ProjectileType.RayoContinuo) ? 0.2f : 0.15f;
                trail.endWidth = 0f;
                trail.sharedMaterial = mat;
                trail.startColor = col;
                trail.endColor = new Color(col.r, col.g, col.b, 0f);
            }
        }

        private void UpdateInspectorValues()
        {
            Type = EffectiveType;
            AutoRandomize = EffectiveAutoRandomize;
            Color = EffectiveColor;
            Damage = EffectiveDamage;
            DetectionRadius = EffectiveDetectionRadius;
            HomingTurnSpeed = EffectiveHomingTurnSpeed;
            LifeTime = EffectiveLifeTime;
            Speed = EffectiveSpeed;
            CorrosionDamage = EffectiveCorrosionDamage;
            DotDuration = EffectiveDotDuration;
            ExplosionRadius = EffectiveExplosionRadius;
            FreezeSlowAmount = EffectiveFreezeSlowAmount;
        }

        private void OnValidate()
        {
            UpdateInspectorValues();
        }

        private void Update()
        {
#if UNITY_EDITOR
            UpdateInspectorValues();
#endif
            if (m_Owner == null && !m_IsAttached) return;
            if (m_IsAttached)
            {
                if (m_AttachedTarget != null)
                {
                    transform.position = m_AttachedTarget.TransformPoint(m_AttachedLocalPos);
                }
                else
                {
                    if (!IsNetworkActive || IsServer) DetonateStickyBomb();
                }
                return;
            }

            m_LifeTimer += Time.deltaTime;
            if (m_LifeTimer >= EffectiveLifeTime && !m_HasHit)
            {
                m_HasHit = true;
                if (!IsNetworkActive || IsServer) FinalizeImpact();
                return;
            }

            if (enableHomingTracking)
            {
                GuideTowardsRivalTarget();
            }

            float moveDistance = EffectiveSpeed * Time.deltaTime;

            if (moveDistance > 0.001f && (!IsNetworkActive || IsServer))
            {
                int n = Physics.SphereCastNonAlloc(transform.position, 0.5f, m_Direction, s_Hits, moveDistance, ~0, QueryTriggerInteraction.Collide);
                System.Array.Sort(s_Hits, 0, n, s_ByDistance);

                for (int i = 0; i < n; i++)
                {
                    var hit = s_Hits[i];
                    if (m_HasHit) break;
                    if (m_Owner != null && (hit.collider.gameObject == m_Owner || hit.collider.transform.IsChildOf(m_Owner.transform))) continue;

                    HealthController targetHealth = hit.collider.GetComponentInParent<HealthController>() ?? hit.collider.GetComponent<HealthController>() ?? hit.collider.GetComponentInChildren<HealthController>() ?? (hit.collider.transform.root != null ? hit.collider.transform.root.GetComponent<HealthController>() : null);

                    if (hit.collider.isTrigger && targetHealth == null) continue;

                    m_HasHit = true;
                    transform.position = hit.point;

                    ProcessImpactEffects(hit.collider, targetHealth);
                    return;
                }
            }

            transform.position += m_Direction * moveDistance;

            if (m_Direction != Vector3.zero)
                transform.forward = m_Direction;
        }

        private void GuideTowardsRivalTarget()
        {
            if (m_HomingTarget == null || !m_HomingTarget.gameObject.activeInHierarchy)
            {
                m_HomingTarget = null;

                Collider[] hits = Physics.OverlapSphere(transform.position, EffectiveDetectionRadius, ~0, QueryTriggerInteraction.Collide);
                float minD = float.MaxValue;

                foreach (var h in hits)
                {
                    if (m_Owner != null && (h.gameObject == m_Owner || h.transform.IsChildOf(m_Owner.transform))) continue;

                    HealthController hc = h.GetComponentInParent<HealthController>() ?? h.GetComponent<HealthController>();
                    if (hc != null)
                    {
                        if (hc.EffectiveCurrentHealth <= 0) continue;

                        bool isOpposing = (m_OwnerTeam != Team.Neutral && hc.EffectiveTeam != m_OwnerTeam) ||
                                          (m_OwnerTeam == Team.Neutral && hc.EffectiveTeam != Team.Neutral) ||
                                          (m_Owner != null && m_Owner.CompareTag("Player") && (h.CompareTag("Enemy") || hc.CompareTag("Enemy"))) ||
                                          (m_Owner != null && m_Owner.CompareTag("Enemy") && (h.CompareTag("Player") || hc.CompareTag("Player")));

                        if (isOpposing)
                        {
                            float d = Vector3.Distance(transform.position, h.transform.position);
                            if (d < minD)
                            {
                                minD = d;
                                m_HomingTarget = h.transform;
                            }
                        }
                    }
                }
            }

            if (m_HomingTarget != null)
            {
                Vector3 targetCenter = m_HomingTarget.position + Vector3.up * 0.9f;
                Vector3 targetDir = (targetCenter - transform.position);
                if (targetDir.sqrMagnitude > 0.01f)
                {
                    m_Direction = Vector3.Slerp(m_Direction, targetDir.normalized, Time.deltaTime * EffectiveHomingTurnSpeed);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsNetworkActive && !IsServer) return;
            if (m_HasHit || m_IsAttached) return;
            if (m_Owner != null && (other.gameObject == m_Owner || other.transform.IsChildOf(m_Owner.transform))) return;

            HealthController targetHealth = other.GetComponentInParent<HealthController>() ?? other.GetComponent<HealthController>() ?? other.GetComponentInChildren<HealthController>() ?? (other.transform.root != null ? other.transform.root.GetComponent<HealthController>() : null);

            if (other.isTrigger && targetHealth == null) return;

            m_HasHit = true;
            ProcessImpactEffects(other, targetHealth);
        }

        private void ProcessImpactEffects(Collider hitCollider, HealthController targetHealth)
        {
            ProjectileType type = EffectiveType;

            if (m_OwnerTeam != Team.Enemy && hitCollider != null)
                hitCollider.GetComponentInParent<Smashable>()?.Damage(EffectiveDamage);

            switch (type)
            {
                case ProjectileType.BalaExplosiva:
                    AttachToTarget(hitCollider);
                    break;

                case ProjectileType.BalaCongelante:
                    if (targetHealth != null)
                    {
                        ApplyDirectDamage(targetHealth);
                        var status = targetHealth.GetComponent<ProjectileStatusReceiver>() ?? targetHealth.gameObject.AddComponent<ProjectileStatusReceiver>();
                        status.ApplyFreeze(EffectiveFreezeSlowAmount, EffectiveDotDuration);
                    }
                    FinalizeImpact();
                    break;

                case ProjectileType.BalaCorrosiva:
                    if (targetHealth != null)
                    {
                        ApplyDirectDamage(targetHealth);
                        var status = targetHealth.GetComponent<ProjectileStatusReceiver>() ?? targetHealth.gameObject.AddComponent<ProjectileStatusReceiver>();
                        status.ApplyCorrosion(EffectiveCorrosionDamage, EffectiveDotDuration);
                    }
                    FinalizeImpact();
                    break;

                case ProjectileType.RayoContinuo:
                case ProjectileType.BalaFuego:
                default:
                    if (targetHealth != null)
                    {
                        ApplyDirectDamage(targetHealth);
                    }
                    FinalizeImpact();
                    break;
            }
        }

        private void AttachToTarget(Collider hitCollider)
        {
            m_IsAttached = true;
            m_AttachedTarget = hitCollider.transform;
            m_AttachedLocalPos = m_AttachedTarget.InverseTransformPoint(transform.position);

            if (!s_ActiveAttachedBombs.Contains(this))
            {
                s_ActiveAttachedBombs.Add(this);
            }

            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        public void DetonateStickyBomb()
        {
            if (this == null) return;

            PerformExplosion();
            FinalizeImpact();
        }

        private void ApplyDirectDamage(HealthController target)
        {
            if (target == null) return;
            if (target.gameObject == m_Owner || (m_Owner != null && target.transform.IsChildOf(m_Owner.transform))) return;
            if (m_OwnerTeam != Team.Neutral && target.EffectiveTeam == m_OwnerTeam) return;

            int finalDmg = Mathf.Max(1, Mathf.RoundToInt(EffectiveDamage));
            target.TakeDamage(finalDmg);
        }

        private void PerformExplosion()
        {
            if (explosionVFX != null) Instantiate(explosionVFX, transform.position, Quaternion.identity);

            Collider[] hits = Physics.OverlapSphere(transform.position, EffectiveExplosionRadius, ~0, QueryTriggerInteraction.Collide);
            foreach (var h in hits)
            {
                if (m_OwnerTeam != Team.Enemy) h.GetComponentInParent<Smashable>()?.Damage(EffectiveDamage);
                HealthController hc = h.GetComponentInParent<HealthController>() ?? h.GetComponent<HealthController>();
                if (hc != null && hc.gameObject != m_Owner)
                {
                    hc.TakeDamage((int)EffectiveDamage);
                }
            }
        }

        private void FinalizeImpact()
        {
            s_ActiveAttachedBombs.Remove(this);

            if (impactVFX != null) Instantiate(impactVFX, transform.position, Quaternion.identity);

            if (IsNetworkActive && IsSpawned)
            {
                if (IsServer) NetworkObject.Despawn(true);   // con el manejador registrado, Despawn(true) devuelve la bala al pool en todos los peers
            }
            else
            {
                if (BalanceManager.Instance != null) BalanceManager.Instance.RecycleToPool(gameObject);
                else gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            s_ActiveAttachedBombs.Remove(this);
            s_ActiveProjectiles.Remove(this);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            s_ActiveAttachedBombs.Remove(this);
            s_ActiveProjectiles.Remove(this);
        }
    }
}