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
        public float maxJetpack = 0f;

        [Header("Visual Feedback (Optional)")]
        public Renderer[] visualsToFlash;
        public Color flashColor = Color.white;
        public float flashDuration = 0.15f;

        [Header("Events")]
        public UnityEvent OnDeath;
        public UnityEvent<int> OnTakeDamage;

        private NetworkVariable<int> currentHealth = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private int m_OfflineHealth;
        private float m_Jetpack;
        private float m_DamageFlashTimer;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        public int CurrentHP => IsNetworkActive ? currentHealth.Value : m_OfflineHealth;
        public float JetpackFuel => m_Jetpack;

        void Awake()
        {
            m_OfflineHealth = maxHealth;
            m_Jetpack = maxJetpack;

            // Auto-detect visuals if not assigned
            if (visualsToFlash == null || visualsToFlash.Length == 0)
                visualsToFlash = GetComponentsInChildren<Renderer>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) currentHealth.Value = maxHealth;
            m_Jetpack = maxJetpack;
        }

        void Update()
        {
            if (m_DamageFlashTimer > 0) m_DamageFlashTimer -= Time.deltaTime;
        }

        public void TakeDamage(int damage)
        {
            if (damage <= 0) return;

            int finalDamage = damage;

            // Integracion con StatsController: Defensa
            if (TryGetComponent<StatsController>(out var stats))
            {
                finalDamage = Mathf.RoundToInt(damage * (10f / (10f + stats.Defense)));
                if (finalDamage < 1) finalDamage = 1;
            }

            if (IsNetworkActive) { if (IsServer) currentHealth.Value = Mathf.Max(0, currentHealth.Value - finalDamage); }
            else m_OfflineHealth = Mathf.Max(0, m_OfflineHealth - finalDamage);

            // Flash de dano (HUD si es player, Body si es enemigo/objeto)
            if (IsOwner && team == Team.Player) m_DamageFlashTimer = 0.6f;
            PlayHitFlash();

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

        public void UpgradeMaxStats(int healthBonus, float jetpackBonus)
        {
            maxHealth += healthBonus;
            maxJetpack += jetpackBonus;

            if (IsNetworkActive)
            {
                if (IsServer) currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + healthBonus);
            }
            else
            {
                m_OfflineHealth = Mathf.Min(maxHealth, m_OfflineHealth + healthBonus);
            }

            AddFuel(jetpackBonus);
        }

        public void UseFuel(float amount) => m_Jetpack = Mathf.Max(0f, m_Jetpack - amount);
        public void AddFuel(float amount) => m_Jetpack = Mathf.Min(maxJetpack, m_Jetpack + amount);

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
