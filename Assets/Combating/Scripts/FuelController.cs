using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for character Jetpack fuel management.
    /// </summary>
    public class FuelController : NetworkBehaviour, IPlayer
    {
        [Header("Jetpack Settings")]
        public float maxJetpack = 100f;

        private float m_Jetpack;

        public float JetpackFuel => m_Jetpack;

        void Awake()
        {
            m_Jetpack = maxJetpack;
            var hub = GetComponentInParent<PlayerController>();
            if (hub != null) Bind(hub);
        }

        public override void OnNetworkSpawn()
        {
            m_Jetpack = maxJetpack;
        }

        public void Bind(PlayerController hub)
        {
            if (hub != null) hub.RegisterModule(this);
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
            // Update Health through HealthController
            var health = GetComponent<HealthController>();
            if (health != null) health.UpgradeMaxHealth(healthBonus);

            maxJetpack += jetpackBonus;
            AddFuel(jetpackBonus);
        }
    }
}
