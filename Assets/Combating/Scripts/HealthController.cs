using UnityEngine;
using Unity.Netcode;
using UnityEngine.Events;

namespace Combating.Scripts
{
    public enum Team { Neutral, Player, Enemy }

    /// <summary>
    /// Universal controller for Health and Team.
    /// Handles life, damage, status and visual feedback.
    /// </summary>
    public class HealthController : NetworkBehaviour
    {
        [Header("Identity & Team")]
        public Team team = Team.Neutral;
        public int maxHealth = 100;

        [Header("Jetpack Settings")]
        public NetworkVariable<float> maxJetpackFuel = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> currentJetpackFuel = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [Header("Visual Feedback (Optional)")]
        public Renderer[] visualsToFlash;
        public Color flashColor = Color.white;
        public float flashDuration = 0.15f;

        [Header("Events")]
        public UnityEvent OnDeath;
        public UnityEvent<int> OnTakeDamage;

        private NetworkVariable<int> currentHealth = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private int m_OfflineHealth;
        private float m_OfflineJetpack;
        private float m_DamageFlashTimer;

        private Animator m_Animator;
        private static readonly int _animIDTakeDamage = Animator.StringToHash("takeDamage");
        private bool _hasAnimDamage;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        public int CurrentHP => IsNetworkActive ? currentHealth.Value : m_OfflineHealth;
        public float JetpackFuel => IsNetworkActive ? currentJetpackFuel.Value : m_OfflineJetpack;
        public float MaxJetpack => IsNetworkActive ? maxJetpackFuel.Value : m_OfflineJetpackMax;
        private float m_OfflineJetpackMax = 100f;

        void Awake()
        {
            m_OfflineHealth = maxHealth;
            m_OfflineJetpack = m_OfflineJetpackMax;

            m_Animator = GetComponentInChildren<Animator>();
            if (m_Animator != null)
            {
                _hasAnimDamage = HasParameter(m_Animator, _animIDTakeDamage);
            }

            // Auto-detect visuals if not assigned
            if (visualsToFlash == null || visualsToFlash.Length == 0)
                visualsToFlash = GetComponentsInChildren<Renderer>();
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
                currentHealth.Value = maxHealth;
                if (maxJetpackFuel.Value <= 0) maxJetpackFuel.Value = 100f;
                currentJetpackFuel.Value = maxJetpackFuel.Value;
            }
        }

        void Update()
        {
            if (m_DamageFlashTimer > 0) m_DamageFlashTimer -= Time.deltaTime;
        }

        public void TakeDamage(int damage)
        {
            if (damage <= 0) return;

            int finalDamage = damage;

            // Integracion con StatsController: Defensa (Intento robusto de encontrar el script en la raiz)
            var stats = GetComponent<HudController>() ?? GetComponentInParent<HudController>();
            if (stats != null)
            {
                finalDamage = Mathf.RoundToInt(damage * (10f / (10f + stats.Defense)));
                if (finalDamage < 1) finalDamage = 1;
            }

            if (IsNetworkActive) { if (IsServer) currentHealth.Value = Mathf.Max(0, currentHealth.Value - finalDamage); }
            else m_OfflineHealth = Mathf.Max(0, m_OfflineHealth - finalDamage);

            // Flash de dano (HUD si es player, Body si es enemigo/objeto)
            if (IsOwner && team == Team.Player) m_DamageFlashTimer = 0.6f;
            PlayHitFlash();

            // Trigger Animator Damage
            if (_hasAnimDamage) m_Animator.SetTrigger(_animIDTakeDamage);

            OnTakeDamage?.Invoke(finalDamage);
            if (CurrentHP <= 0) Die();
        }

        private void PlayHitFlash()
        {
            if (visualsToFlash != null && visualsToFlash.Length > 0)
            {
                foreach (var r in visualsToFlash)
                {
                    if (r == null) continue;
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_EmissionColor", flashColor * 2f);
                    r.SetPropertyBlock(mpb);
                }
                Invoke(nameof(ResetFlash), flashDuration);
            }
        }

        private void ResetFlash()
        {
            if (visualsToFlash != null)
            {
                foreach (var r in visualsToFlash)
                {
                    if (r != null) r.SetPropertyBlock(null);
                }
            }
        }

        public void Heal(int amount)
        {
            if (amount <= 0) return;

            if (IsNetworkActive)
            {
                if (IsServer) currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + amount);
            }
            else
            {
                m_OfflineHealth = Mathf.Min(maxHealth, m_OfflineHealth + amount);
            }

            Debug.Log($"[Health] Recuperada {amount} HP. Vida actual: {CurrentHP}");
        }

        public void UpgradeMaxStats(int healthBonus, float jetpackBonus)
        {
            maxHealth += healthBonus;

            if (IsNetworkActive)
            {
                if (IsServer)
                {
                    currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + healthBonus);
                    maxJetpackFuel.Value += jetpackBonus;
                    currentJetpackFuel.Value = Mathf.Min(maxJetpackFuel.Value, currentJetpackFuel.Value + jetpackBonus);
                }
            }
            else
            {
                m_OfflineHealth = Mathf.Min(maxHealth, m_OfflineHealth + healthBonus);
                m_OfflineJetpackMax += jetpackBonus;
                m_OfflineJetpack = Mathf.Min(m_OfflineJetpackMax, m_OfflineJetpack + jetpackBonus);
            }
        }

        public void UseFuel(float amount)
        {
            if (IsNetworkActive)
            {
                if (IsServer) currentJetpackFuel.Value = Mathf.Max(0f, currentJetpackFuel.Value - amount);
                else UseFuelServerRpc(amount);
            }
            else m_OfflineJetpack = Mathf.Max(0f, m_OfflineJetpack - amount);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void UseFuelServerRpc(float amount) => UseFuel(amount);

        public void AddFuel(float amount)
        {
            if (IsNetworkActive)
            {
                if (IsServer) currentJetpackFuel.Value = Mathf.Min(maxJetpackFuel.Value, currentJetpackFuel.Value + amount);
                else AddFuelServerRpc(amount);
            }
            else m_OfflineJetpack = Mathf.Min(m_OfflineJetpackMax, m_OfflineJetpack + amount);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void AddFuelServerRpc(float amount) => AddFuel(amount);

        private void Die()
        {
            OnDeath?.Invoke();
            if (TryGetComponent<SpawnController>(out var sc)) sc.TriggerDeath();
            else
            {
                if (IsNetworkActive && IsServer && IsSpawned)
                {
                    GetComponent<NetworkObject>().Despawn(false);
                    Destroy(gameObject);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }

        #region UI Effects
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!IsOwner || team != Team.Player) return;

            float sw = Screen.width;
            float sh = Screen.height;

            if (m_DamageFlashTimer > 0)
            {
                GUI.color = new Color(1, 0, 0, m_DamageFlashTimer * 0.8f);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            if (CurrentHP < maxHealth * 0.25f && CurrentHP > 0)
            {
                float pulse = Mathf.PingPong(Time.time * 2.5f, 0.25f);
                GUI.color = new Color(1, 0, 0, pulse);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }
        #endregion
    }
}
