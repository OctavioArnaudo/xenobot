using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public enum Team { Neutral, Player, Enemy }

    /// <summary>
    /// Specialized controller for character Health state and Team identity.
    /// Acts as the data source for life status.
    /// </summary>
    public class HealthController : NetworkBehaviour, IPlayerModule
    {
        [Header("Identity & Team")]
        public Team team = Team.Neutral;
        public int maxHealth = 100;

        private NetworkVariable<int> currentHealth = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private int m_OfflineHealth;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        public int CurrentHP => IsNetworkActive ? currentHealth.Value : m_OfflineHealth;

        void Awake()
        {
            m_OfflineHealth = maxHealth;
            var hub = GetComponentInParent<PlayerController>();
            if (hub != null) Bind(hub);
        }

        public void Bind(PlayerController hub)
        {
            if (hub != null) hub.RegisterModule(this);
        }

        public void OnRefreshModule() { }

        public override void OnNetworkSpawn()
        {
            if (IsServer) currentHealth.Value = maxHealth;
        }

        public void ApplyDirectHealthChange(int amount)
        {
            if (IsNetworkActive)
            {
                if (IsServer) currentHealth.Value = Mathf.Clamp(currentHealth.Value + amount, 0, maxHealth);
            }
            else
            {
                m_OfflineHealth = Mathf.Clamp(m_OfflineHealth + amount, 0, maxHealth);
            }
        }

        public void Heal(int amount)
        {
            if (amount <= 0) return;
            ApplyDirectHealthChange(amount);
            Debug.Log($"[Health] Recuperada {amount} HP. Vida actual: {CurrentHP}");
        }

        public void UpgradeMaxHealth(int bonus)
        {
            maxHealth += bonus;
            ApplyDirectHealthChange(bonus);
        }
    }
}
