using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.Events;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Effective;
using Runtime.Scripts.Player;
using Runtime.Scripts.Enemy;
using Runtime.Scripts.Shields;
using Runtime.Scripts.Singleton;

namespace Runtime.Scripts.Health
{
    public enum Team { Neutral, Player, Enemy }

    /// <summary>
    /// Universal controller for Health, Team, and Shield Mitigation with IFrames support.
    /// Includes automatic floating world-space health bars for enemies.
    /// </summary>
    public class HealthController : NetworkBehaviour, IBalanceCalibratable
    {
        public static HealthController LocalInstance => CSingleton<HealthController>.Instance;
        public StatSpawnPointData CurrentStats { get; private set; }
        public event System.Action<StatSpawnPointData> OnStatsUpdated;

        public void CalibrateStats(StatSpawnPointData stats)
        {
            CurrentStats = stats;
            if (!maxHealth.use && stats.maxHealth.use && stats.maxHealth.value > 0)
            {
                MaxHealth = stats.maxHealth.value;
            }
            if (!currentHealth.use && stats.currentHealth.use && stats.currentHealth.value >= 0)
            {
                SetHealth(stats.currentHealth.value);
            }
            ResetEnemyHealthBar();
            OnStatsUpdated?.Invoke(stats);
        }

        // --- Internal Hardcoded Health Defaults ---
        private const int DEFAULT_PLAYER_MAX_HEALTH = 250;
        private const int DEFAULT_ENEMY_BASE_HEALTH = 120;

        [Header("Identity & Team")]
        public Team team = Team.Neutral;

        [Header("Health & Invulnerability (Iframes)")]
        public int MaxHealth;
        public Optional<int> maxHealth;
        public int CurrentHealth;
        public Optional<int> currentHealth;
        [Tooltip("Duración en segundos de la invulnerabilidad (iframes) tras recibir daño.")]
        public float invulnerabilityDuration = 0.5f;

        [Header("Enemy Floating Health Bar")]
        public bool showEnemyHealthBar = true;
        public bool hideBarWhenFull = true;
        public Vector3 healthBarOffset = new Vector3(0f, 0.45f, 0f);
        public Vector2 healthBarSize = new Vector2(1.1f, 0.14f);
        public float healthBarMaxDistance = 35f;

        [Header("Visual Feedback (Optional)")]
        public Renderer[] visualsToFlash;
        public Color flashColor = Color.red;
        public float flashDuration = 0.15f;

        [Header("Events")]
        public UnityEvent OnDeath;
        public UnityEvent<int> OnTakeDamage;

        private float m_DamageFlashTimer;
        private float m_InvulnerabilityTimer = 0f;

        // Caché de colores originales para restaurar exactamente la apariencia sin decolorar al personaje
        private Dictionary<Material, Color> m_OriginalColors = new Dictionary<Material, Color>();

        private Animator m_Animator;
        private static readonly int _animIDTakeDamage = Animator.StringToHash("takeDamage");
        private bool _hasAnimDamage;

        // --- Floating Enemy Health Bar State ---
        private GameObject _hpBarCanvasObj;
        private RectTransform _hpFillRect;
        private RectTransform _hpTrailRect;
        private Transform _hpAnchorTarget;
        private Vector3 _hpLocalAnchorOffset;
        private Camera _hpMainCamCache;
        private float _hpCurrentFill = 1f;
        private float _hpTrailFill = 1f;
        private float _hpTrailHoldTimer = 0f;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        public bool IsInvulnerable => m_InvulnerabilityTimer > 0f;

        public Team EffectiveTeam
        {
            get
            {
                if (team != Team.Neutral) return team;

                var pc = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>() ?? GetComponentInChildren<PlayerController>();
                if (pc != null || CompareTag("Player") || transform.root.CompareTag("Player")) return Team.Player;

                var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>() ?? GetComponentInChildren<EnemyController>();
                if (enemy != null || CompareTag("Enemy") || transform.root.CompareTag("Enemy")) return Team.Enemy;

                return Team.Neutral;
            }
        }

        public int EffectiveMaxHealth
        {
            get
            {
                if (maxHealth.use) return maxHealth.value;
                int defaultVal = CalculateDynamicHealthBalance();
                Optional<int> balanceOpt = CurrentStats.maxHealth.use ? CurrentStats.maxHealth : (BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject).maxHealth : default);
                return maxHealth.Get(MaxHealth, defaultVal, balanceOpt);
            }
        }

        public int EffectiveCurrentHealth
        {
            get
            {
                if (currentHealth.use) return currentHealth.value;
                int defaultVal = EffectiveMaxHealth;
                Optional<int> balanceOpt = CurrentStats.currentHealth.use ? CurrentStats.currentHealth : (BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject).currentHealth : default);
                return currentHealth.Get(CurrentHealth, defaultVal, balanceOpt);
            }
        }

        private int CalculateDynamicHealthBalance()
        {
            var pc = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
            if (pc != null || CompareTag("Player"))
            {
                return DEFAULT_PLAYER_MAX_HEALTH;
            }

            var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
            if (enemy != null)
            {
                float baseHp = DEFAULT_ENEMY_BASE_HEALTH;

                switch (enemy.activeArchetype)
                {
                    case AIArchetype.GuardiaConEscudo:
                        baseHp *= 2.2f;
                        break;
                    case AIArchetype.CargaFrenetica:
                        baseHp *= 1.6f;
                        break;
                    case AIArchetype.InvocadorRefuerzos:
                        baseHp *= 1.2f;
                        break;
                    case AIArchetype.EmboscadaEnSigilo:
                        baseHp *= 0.85f;
                        break;
                    case AIArchetype.AtaqueYHuida:
                    case AIArchetype.FlanqueoYCobertura:
                        baseHp *= 0.9f;
                        break;
                    default:
                        baseHp *= 1.0f;
                        break;
                }

                int nearbyAllies = CountNearbyAllies();
                int nearbyPlayers = CountNearbyPlayers();

                if (nearbyAllies >= 3)
                {
                    baseHp *= 0.8f;
                }
                else if (nearbyAllies == 0 && nearbyPlayers >= 1)
                {
                    baseHp *= 1.4f;
                }

                return Mathf.RoundToInt(baseHp);
            }

            return DEFAULT_ENEMY_BASE_HEALTH;
        }

        private int CountNearbyAllies()
        {
            int count = 0;
            var enemies = GameObject.FindGameObjectsWithTag("Enemy");
            foreach (var e in enemies)
            {
                if (e != gameObject && Vector3.Distance(transform.position, e.transform.position) <= 25f) count++;
            }
            return count;
        }

        private int CountNearbyPlayers()
        {
            int count = 0;
            var players = GameObject.FindGameObjectsWithTag("Player");
            foreach (var p in players)
            {
                if (Vector3.Distance(transform.position, p.transform.position) <= 25f) count++;
            }
            return count;
        }

        void Awake()
        {
            CurrentHealth = EffectiveMaxHealth;

            bool isPlayer = CompareTag("Player") || GetComponent<PlayerController>() != null;
            if (isPlayer && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            {
                CSingleton<HealthController>.Instance = this;
            }

            m_Animator = GetComponentInChildren<Animator>();
            if (m_Animator != null)
            {
                _hasAnimDamage = HasParameter(m_Animator, _animIDTakeDamage);
            }

            CacheOriginalColors();
        }

        private void Start()
        {
            if (showEnemyHealthBar && EffectiveTeam == Team.Enemy)
            {
                InitEnemyHealthBar();
            }
        }

        private void OnEnable()
        {
            // Al salir de un Object Pool, restaurar el estado visual de la barra
            if (_hpBarCanvasObj != null)
            {
                ResetEnemyHealthBar();
            }
        }

        private void OnDisable()
        {
            if (_hpBarCanvasObj != null)
            {
                _hpBarCanvasObj.SetActive(false);
            }
        }

        private void CacheOriginalColors()
        {
            if (visualsToFlash == null || visualsToFlash.Length == 0)
                visualsToFlash = GetComponentsInChildren<Renderer>();

            if (visualsToFlash != null)
            {
                foreach (var r in visualsToFlash)
                {
                    if (r == null) continue;
                    foreach (var mat in r.materials)
                    {
                        if (mat == null || m_OriginalColors.ContainsKey(mat)) continue;

                        if (mat.HasProperty("_Color")) m_OriginalColors[mat] = mat.color;
                        else if (mat.HasProperty("_BaseColor")) m_OriginalColors[mat] = mat.GetColor("_BaseColor");
                    }
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
            bool isPlayer = CompareTag("Player") || GetComponent<PlayerController>() != null;
            if (isPlayer && IsOwner)
            {
                CSingleton<HealthController>.Instance = this;
            }

            if (showEnemyHealthBar && EffectiveTeam == Team.Enemy && _hpBarCanvasObj == null)
            {
                InitEnemyHealthBar();
            }
        }

        public override void OnNetworkDespawn()
        {
            CSingleton<HealthController>.UnregisterInstance(this);
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            CSingleton<HealthController>.UnregisterInstance(this);
            if (_hpBarCanvasObj != null) Destroy(_hpBarCanvasObj);
            base.OnDestroy();
        }

        private void UpdateInspectorValues()
        {
            MaxHealth = EffectiveMaxHealth;
            if (!Application.isPlaying)
            {
                CurrentHealth = EffectiveCurrentHealth;
            }
        }

        private void OnValidate()
        {
            UpdateInspectorValues();
        }

        void Update()
        {
#if UNITY_EDITOR
            UpdateInspectorValues();
#endif
            // Actualizar temporizador de Iframes
            if (m_InvulnerabilityTimer > 0)
            {
                m_InvulnerabilityTimer -= Time.deltaTime;
            }

            if (m_DamageFlashTimer > 0)
            {
                m_DamageFlashTimer -= Time.deltaTime;
                if (m_DamageFlashTimer <= 0)
                {
                    // Restablecer los colores ORIGINALES exactos
                    foreach (var kvp in m_OriginalColors)
                    {
                        if (kvp.Key == null) continue;
                        if (kvp.Key.HasProperty("_Color")) kvp.Key.color = kvp.Value;
                        if (kvp.Key.HasProperty("_BaseColor")) kvp.Key.SetColor("_BaseColor", kvp.Value);
                    }
                }
            }
        }

        private void LateUpdate()
        {
            UpdateEnemyHealthBar();
        }

        private void TriggerDamageFlash()
        {
            CacheOriginalColors();

            if (visualsToFlash != null)
            {
                foreach (var r in visualsToFlash)
                {
                    if (r == null) continue;
                    foreach (var mat in r.materials)
                    {
                        if (mat == null) continue;
                        if (mat.HasProperty("_Color")) mat.color = flashColor;
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", flashColor);
                    }
                }
            }
            m_DamageFlashTimer = flashDuration;

            if (m_Animator != null && _hasAnimDamage)
            {
                m_Animator.SetTrigger(_animIDTakeDamage);
            }
        }

        public void SetHealth(int newHealth)
        {
            if (currentHealth.use)
            {
                currentHealth.value = newHealth;
            }
            CurrentHealth = newHealth;
        }

        public void TakeDamage(int damage)
        {
            // Ignorar daño si está en periodo de Iframes (invulnerable)
            if (damage <= 0 || IsInvulnerable) return;

            int finalDamage = damage;

            var shield = GetComponent<ShieldsController>() ?? GetComponentInParent<ShieldsController>();
            if (shield != null && shield.IsShieldActive)
            {
                finalDamage = Mathf.RoundToInt(shield.ProcessIncomingDamage(finalDamage));
            }

            if (finalDamage <= 0) return;

            // Activar el temporizador de invulnerabilidad (Iframes)
            m_InvulnerabilityTimer = invulnerabilityDuration;

            int newHp = Mathf.Max(0, EffectiveCurrentHealth - finalDamage);
            SetHealth(newHp);

            // Retener un instante la barra secundaria amarilla para visualizar el golpe
            _hpTrailHoldTimer = 0.25f;

            TriggerDamageFlash();
            OnTakeDamage?.Invoke(finalDamage);

            if (EffectiveCurrentHealth <= 0)
            {
                if (_hpBarCanvasObj != null) _hpBarCanvasObj.SetActive(false);
                OnDeath?.Invoke();
                if (BalanceManager.Instance != null) BalanceManager.Instance.RecycleToPool(gameObject);
                else gameObject.SetActive(false);
            }
        }

        public void Heal(int amount)
        {
            if (amount <= 0) return;

            int newHp = Mathf.Min(EffectiveMaxHealth, EffectiveCurrentHealth + amount);
            SetHealth(newHp);
        }

        public void UpgradeMaxStats(int healthBonus)
        {
            if (maxHealth.use)
            {
                maxHealth.value += healthBonus;
            }
            else
            {
                MaxHealth += healthBonus;
            }

            int newHp = Mathf.Min(EffectiveMaxHealth, EffectiveCurrentHealth + healthBonus);
            SetHealth(newHp);
        }

        #region Enemy Floating Health Bar
        private void InitEnemyHealthBar()
        {
            if (_hpBarCanvasObj != null) return;

            // 1. Buscar anclaje sobre la cabeza del enemigo
            Animator anim = GetComponentInChildren<Animator>();
            Transform headBone = null;
            if (anim != null && anim.isHuman)
            {
                headBone = anim.GetBoneTransform(HumanBodyBones.Head);
            }
            if (headBone == null)
            {
                foreach (Transform child in GetComponentsInChildren<Transform>())
                {
                    if (child.name.ToLower().Contains("head"))
                    {
                        headBone = child;
                        break;
                    }
                }
            }

            if (headBone != null)
            {
                _hpAnchorTarget = headBone;
                _hpLocalAnchorOffset = healthBarOffset;
            }
            else
            {
                _hpAnchorTarget = transform;
                float topY = 2.0f;
                if (TryGetComponent<CharacterController>(out var cc)) topY = cc.height;
                else if (TryGetComponent<Collider>(out var col)) topY = col.bounds.size.y;

                _hpLocalAnchorOffset = new Vector3(healthBarOffset.x, topY + healthBarOffset.y, healthBarOffset.z);
            }

            // 2. Crear Canvas World-Space
            _hpBarCanvasObj = new GameObject("EnemyHealthBarCanvas");
            _hpBarCanvasObj.transform.SetParent(transform);

            Canvas canvas = _hpBarCanvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 50;

            RectTransform canvasRect = _hpBarCanvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = healthBarSize;
            canvasRect.localScale = Vector3.one;

            // Fondo oscuro
            GameObject bgObj = new GameObject("Background", typeof(Image));
            bgObj.transform.SetParent(_hpBarCanvasObj.transform, false);
            Image bgImg = bgObj.GetComponent<Image>();
            bgImg.color = new Color(0.05f, 0.05f, 0.05f, 0.85f);
            bgImg.raycastTarget = false;
            StretchBarRect(bgObj.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);

            Vector2 padding = new Vector2(0.02f, 0.02f);

            // Barra secundaria (rastro de daño reciente en amarillo)
            GameObject trailObj = new GameObject("DamageTrail", typeof(Image));
            trailObj.transform.SetParent(bgObj.transform, false);
            Image trailImg = trailObj.GetComponent<Image>();
            trailImg.color = new Color(1f, 0.82f, 0.25f, 0.9f);
            trailImg.raycastTarget = false;
            _hpTrailRect = trailObj.GetComponent<RectTransform>();
            StretchBarRect(_hpTrailRect, padding, -padding);

            // Barra principal de vida actual (rojo intenso)
            GameObject fillObj = new GameObject("HealthFill", typeof(Image));
            fillObj.transform.SetParent(bgObj.transform, false);
            Image fillImg = fillObj.GetComponent<Image>();
            fillImg.color = new Color(0.9f, 0.15f, 0.15f, 1f);
            fillImg.raycastTarget = false;
            _hpFillRect = fillObj.GetComponent<RectTransform>();
            StretchBarRect(_hpFillRect, padding, -padding);

            ResetEnemyHealthBar();
        }

        private static void StretchBarRect(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private void ResetEnemyHealthBar()
        {
            float maxHp = Mathf.Max(1f, EffectiveMaxHealth);
            float ratio = Mathf.Clamp01(EffectiveCurrentHealth / maxHp);
            _hpCurrentFill = ratio;
            _hpTrailFill = ratio;
            _hpTrailHoldTimer = 0f;

            if (_hpFillRect != null) _hpFillRect.anchorMax = new Vector2(_hpCurrentFill, 1f);
            if (_hpTrailRect != null) _hpTrailRect.anchorMax = new Vector2(_hpTrailFill, 1f);
        }

        private void UpdateEnemyHealthBar()
        {
            if (_hpBarCanvasObj == null) return;

            int curHp = EffectiveCurrentHealth;
            int maxHp = Mathf.Max(1, EffectiveMaxHealth);

            if (!showEnemyHealthBar || curHp <= 0 || EffectiveTeam != Team.Enemy)
            {
                if (_hpBarCanvasObj.activeSelf) _hpBarCanvasObj.SetActive(false);
                return;
            }

            if (_hpMainCamCache == null) _hpMainCamCache = Camera.main;
            if (_hpMainCamCache == null) return;

            Vector3 worldPos = (_hpAnchorTarget != null ? _hpAnchorTarget.position : transform.position) + _hpLocalAnchorOffset;
            float sqrDist = (worldPos - _hpMainCamCache.transform.position).sqrMagnitude;
            bool inRange = sqrDist <= healthBarMaxDistance * healthBarMaxDistance;
            bool isFull = curHp >= maxHp;

            bool shouldShow = inRange && (!hideBarWhenFull || !isFull);
            if (_hpBarCanvasObj.activeSelf != shouldShow)
            {
                _hpBarCanvasObj.SetActive(shouldShow);
            }

            if (!shouldShow) return;

            // Posicionar sobre la cabeza y rotar hacia la cámara (Billboard)
            _hpBarCanvasObj.transform.position = worldPos;
            _hpBarCanvasObj.transform.rotation = Quaternion.LookRotation(_hpBarCanvasObj.transform.position - _hpMainCamCache.transform.position);

            // Actualizar relleno principal y drenaje suave de la barra de daño reciente
            _hpCurrentFill = Mathf.Clamp01((float)curHp / maxHp);

            if (_hpTrailFill > _hpCurrentFill)
            {
                if (_hpTrailHoldTimer > 0f)
                {
                    _hpTrailHoldTimer -= Time.deltaTime;
                }
                else
                {
                    _hpTrailFill = Mathf.MoveTowards(_hpTrailFill, _hpCurrentFill, 2.2f * Time.deltaTime);
                }
            }
            else
            {
                _hpTrailFill = _hpCurrentFill;
            }

            if (_hpFillRect != null) _hpFillRect.anchorMax = new Vector2(_hpCurrentFill, 1f);
            if (_hpTrailRect != null) _hpTrailRect.anchorMax = new Vector2(_hpTrailFill, 1f);
        }
        #endregion
    }
}