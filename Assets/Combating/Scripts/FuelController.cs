using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for character Jetpack fuel management.
    /// </summary>
    public class FuelController : NetworkBehaviour, IModular
    {
        [Header("Jetpack Settings")]
        public float maxJetpack = 100f;

        private float m_Jetpack;
        private ModularController _hub;

        public float JetpackFuel => m_Jetpack;

        void Awake()
        {
            m_Jetpack = maxJetpack;
            _hub = GetComponentInParent<ModularController>();
            if (_hub != null) Bind(_hub);
        }

        public override void OnNetworkSpawn()
        {
            m_Jetpack = maxJetpack;
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null) _hub.RegisterModule(this);
        }

        public void OnRefreshModule() { }

        public void UseFuel(float amount)
        {
            m_Jetpack = Mathf.Max(0f, m_Jetpack - amount);
        }

        public void AddFuel(float amount)
        {
            m_Jetpack = Mathf.Min(maxJetpack, m_Jetpack + amount);
        }

        public void UpgradeMaxStats(int healthBonus, float jetpackBonus)
        {
            var health = (_hub != null) ? _hub.GetModule<HealthController>() : GetComponent<HealthController>();
            if (health != null) health.UpgradeMaxHealth(healthBonus);

            maxJetpack += jetpackBonus;
            AddFuel(jetpackBonus);
        }
    }
}
