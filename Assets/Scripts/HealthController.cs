using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.Events;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Effective;
using Runtime.Scripts.Player;
using Runtime.Scripts.Enemy;
using Runtime.Scripts.Shield;
using Runtime.Scripts.Singleton;

namespace Runtime.Scripts.Health
{
    public enum Team { Neutral, Player, Enemy }

    /// <summary>
    /// Universal controller for Health, Team, and Shield Mitigation with IFrames support.
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
        }

        public override void OnNetworkDespawn()
        {
            CSingleton<HealthController>.UnregisterInstance(this);
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            CSingleton<HealthController>.UnregisterInstance(this);
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

            var shield = GetComponent<ShieldController>() ?? GetComponentInParent<ShieldController>();
            if (shield != null && shield.IsShieldActive)
            {
                finalDamage = Mathf.RoundToInt(shield.ProcessIncomingDamage(finalDamage));
            }

            if (finalDamage <= 0) return;

            // Activar el temporizador de invulnerabilidad (Iframes)
            m_InvulnerabilityTimer = invulnerabilityDuration;

            int newHp = Mathf.Max(0, EffectiveCurrentHealth - finalDamage);
            SetHealth(newHp);

            TriggerDamageFlash();
            OnTakeDamage?.Invoke(finalDamage);

            if (EffectiveCurrentHealth <= 0)
            {
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
    }
}