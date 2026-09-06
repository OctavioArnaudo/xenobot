using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;
using Combating.Scripts;

namespace Testing.Scripts
{
    public class HealthController : MonoBehaviour, IModular
    {
        private ModularController _hub;
        private AnimationController _anim;

        private int m_OfflineHealth;

        public int CurrentHP => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? _hub.currentHealth.Value : m_OfflineHealth;
        public int maxHealth => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? _hub.maxHealth.Value : m_OfflineHealth;

        void Awake()
        {
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);

                // Initialize health immediately if offline
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                {
                    RandomizeHealthOffline();
                }

                OnRefreshModule();
            }
        }

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                _anim = _hub.GetModule<AnimationController>();
            }
        }

        public void RandomizeHealthOffline()
        {
            if (_hub is PlayerController)
            {
                m_OfflineHealth = Random.Range(110, 136);
                if (_hub.IsOwner) m_OfflineHealth += 15;
            }
            else if (_hub is EnemyController)
            {
                m_OfflineHealth = Random.Range(65, 96);
            }
        }

        public void ApplyDirectHealthChange(int amount)
        {
            if (_hub == null) return;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                _hub.ApplyHealthChangeServerRpc(amount);
            }
            else
            {
                m_OfflineHealth = Mathf.Clamp(m_OfflineHealth + amount, 0, 999);
            }

            if (amount < 0 && _anim != null) _anim.TriggerTakeDamage();
        }

        public void UpgradeMaxHealth(int bonus)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                // Managed by Hub on LevelUp RPC
            }
            else
            {
                m_OfflineHealth += bonus;
            }
        }
    }
}
