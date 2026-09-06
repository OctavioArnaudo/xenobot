using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for character Jetpack fuel storage and management.
    /// Acts as the central tank for the entity.
    /// </summary>
    public class TankController : MonoBehaviour, IModular
    {
        private float m_OfflineFuel = 100f;
        private float m_OfflineMaxFuel = 100f;
        private ModularController _hub;

        public float JetpackFuel => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? _hub.currentFuel.Value : m_OfflineFuel;
        public float maxJetpack => (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) ? _hub.maxFuel.Value : m_OfflineMaxFuel;

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
                OnRefreshModule();
            }
        }

        public void OnRefreshModule() { }

        public void UseFuel(float amount)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (_hub.IsServer) _hub.currentFuel.Value = Mathf.Max(0f, _hub.currentFuel.Value - amount);
            }
            else
            {
                m_OfflineFuel = Mathf.Max(0f, m_OfflineFuel - amount);
            }
        }

        public void AddFuel(float amount)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (_hub.IsServer) _hub.currentFuel.Value = Mathf.Min(_hub.maxFuel.Value, _hub.currentFuel.Value + amount);
            }
            else
            {
                m_OfflineFuel = Mathf.Min(m_OfflineMaxFuel, m_OfflineFuel + amount);
            }
        }

        public void UpgradeMaxStats(int healthBonus, float jetpackBonus)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                m_OfflineMaxFuel += jetpackBonus;
                AddFuel(jetpackBonus);
            }
        }
    }
}
