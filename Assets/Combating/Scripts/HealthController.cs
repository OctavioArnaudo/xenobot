using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.Events;

namespace Combating.Scripts
{
    public enum Team { Neutral, Player, Enemy }

    /// <summary>
    /// Universal controller for Health, Team, and Shield Mitigation.
    /// </summary>
    public class HealthController : NetworkBehaviour
    {
        // --- Internal Hardcoded Health Defaults ---
        private const int DEFAULT_PLAYER_MAX_HEALTH = 250;
        private const int DEFAULT_ENEMY_BASE_HEALTH = 120;

        [Header("Identity & Team Overrides (Neutral = Auto-detectar)")]
        public Team team = Team.Neutral;

        [Header("Manual Health Override (0 = Usar Balance Dinámico Interno)")]
        public int maxHealth = 0;

        [Header("Visual Feedback (Optional)")]
        public Renderer[] visualsToFlash;
        public Color flashColor = Color.red;
        public float flashDuration = 0.15f;

        [Header("Events")]
        public UnityEvent OnDeath;
        public UnityEvent<int> OnTakeDamage;

        private NetworkVariable<int> currentHealth = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private int m_OfflineHealth;
        private float m_DamageFlashTimer;

        // Caché de colores originales para restaurar exactamente la apariencia sin decolorar al personaje
        private Dictionary<Material, Color> m_OriginalColors = new Dictionary<Material, Color>();

        private Animator m_Animator;
        private static readonly int _animIDTakeDamage = Animator.StringToHash("takeDamage");
        private bool _hasAnimDamage;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        public Team EffectiveTeam
        {
            get
            {
                if (team != Team.Neutral) return team;

                var pc = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
                if (pc != null || CompareTag("Player")) return Team.Player;

                var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
                if (enemy != null || CompareTag("Enemy")) return Team.Enemy;

                return Team.Neutral;
            }
        }

        public int EffectiveMaxHealth
        {
            get
            {
                try
                {
                    if (maxHealth > 0) return maxHealth;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[Fallback] maxHealth en {gameObject.name}: {ex.Message}");
                }

                return CalculateDynamicHealthBalance();
            }
        }

        public int CurrentHP => IsNetworkActive ? currentHealth.Value : m_OfflineHealth;

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
            m_OfflineHealth = EffectiveMaxHealth;

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
            if (IsServer)
            {
                currentHealth.Value = EffectiveMaxHealth;
            }

            currentHealth.OnValueChanged += (oldVal, newVal) =>
            {
                int diff = oldVal - newVal;
                if (diff > 0)
                {
                    TriggerDamageFlash();
                    OnTakeDamage?.Invoke(diff);
                }

                if (newVal <= 0)
                {
                    OnDeath?.Invoke();
                }
            };
        }

        public override void OnNetworkDespawn()
        {
            currentHealth.OnValueChanged -= (oldVal, newVal) => { };
            base.OnNetworkDespawn();
        }

        void Update()
        {
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

        public void TakeDamage(int damage)
        {
            if (damage <= 0) return;

            int finalDamage = damage;

            var shield = GetComponent<ShieldController>() ?? GetComponentInParent<ShieldController>();
            if (shield != null && shield.IsShieldActive)
            {
                finalDamage = Mathf.RoundToInt(shield.ProcessIncomingDamage(finalDamage));
            }

            if (finalDamage <= 0) return;

            if (IsNetworkActive)
            {
                if (IsServer) ApplyDamageServer(finalDamage);
                else RequestTakeDamageServerRpc(finalDamage);
            }
            else
            {
                ApplyDamageLocal(finalDamage);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestTakeDamageServerRpc(int damage)
        {
            ApplyDamageServer(damage);
        }

        private void ApplyDamageServer(int damage)
        {
            currentHealth.Value = Mathf.Max(0, currentHealth.Value - damage);
            TriggerDamageFlash();
            OnTakeDamage?.Invoke(damage);

            if (currentHealth.Value <= 0)
            {
                OnDeath?.Invoke();
            }
        }

        private void ApplyDamageLocal(int damage)
        {
            m_OfflineHealth = Mathf.Max(0, m_OfflineHealth - damage);
            TriggerDamageFlash();
            OnTakeDamage?.Invoke(damage);

            if (m_OfflineHealth <= 0)
            {
                OnDeath?.Invoke();
            }
        }

        public void Heal(int amount)
        {
            if (amount <= 0) return;

            if (IsNetworkActive)
            {
                if (IsServer) currentHealth.Value = Mathf.Min(EffectiveMaxHealth, currentHealth.Value + amount);
                else HealServerRpc(amount);
            }
            else
            {
                m_OfflineHealth = Mathf.Min(EffectiveMaxHealth, m_OfflineHealth + amount);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void HealServerRpc(int amount) => Heal(amount);

        public void UpgradeMaxStats(int healthBonus)
        {
            if (IsNetworkActive)
            {
                if (IsServer)
                {
                    currentHealth.Value += healthBonus;
                }
            }
            else
            {
                m_OfflineHealth += healthBonus;
            }
        }
    }
}
