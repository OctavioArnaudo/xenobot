using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class PropulsionController : MonoBehaviour, IItemFunctional, IModular
    {
        [Header("Flight Settings")]
        public float jetpackForce = 60f;
        public float hoverForce = 25f;
        public float fuelConsumption = 30f;
        public float fuelRegen = 15f;
        public float maxUpwardVelocity = 12f;
        public float hoverThreshold = 0.5f;

        private FuelController m_Fuel;
        private CharacterController m_CharController;
        private ModularController _hub;
        private bool m_IsUsingJetpack = false;
        private bool m_JetpackDepleted = false;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private void Awake()
        {
            _hub = GetComponentInParent<ModularController>();
            if (_hub != null) Bind(_hub);
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

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                m_Fuel = _hub.GetModule<FuelController>();
                m_CharController = _hub.controller ?? _hub.GetComponent<CharacterController>();
            }
        }

        public void ApplyEffect(GameObject player)
        {
            _hub = player.GetComponent<ModularController>();
            if (_hub != null) Bind(_hub);

            if (m_Fuel != null && m_Fuel.maxJetpack <= 0)
            {
                m_Fuel.maxJetpack = 100f;
                m_Fuel.AddFuel(100f);
            }
        }

        public bool ProcessFlight(bool isJumpHeld, bool isGrounded, ref float verticalVelocity)
        {
            if (_hub == null) return false;

            bool isOwner = IsNetworkActive ? _hub.IsOwner : true;
            if (!isOwner) return false;

            m_IsUsingJetpack = false;
            if (isGrounded)
            {
                m_JetpackDepleted = false;
                if (m_Fuel != null) m_Fuel.AddFuel(fuelRegen * Time.deltaTime);
                return false;
            }

            if (!isJumpHeld) m_JetpackDepleted = false;
            if (m_Fuel != null && m_Fuel.JetpackFuel <= 0) m_JetpackDepleted = true;

            if (isJumpHeld && !m_JetpackDepleted && m_Fuel != null && m_Fuel.JetpackFuel > 0)
            {
                m_IsUsingJetpack = true;
                if (verticalVelocity < -2f) verticalVelocity = Mathf.MoveTowards(verticalVelocity, 0, Time.deltaTime * 20f);
                float currentForce = (verticalVelocity > hoverThreshold) ? hoverForce : jetpackForce;
                verticalVelocity += currentForce * Time.deltaTime;
                if (verticalVelocity > maxUpwardVelocity) verticalVelocity = maxUpwardVelocity;
                m_Fuel.UseFuel(fuelConsumption * Time.deltaTime);
            }
            else if (m_Fuel != null) m_Fuel.AddFuel((fuelRegen * 0.2f) * Time.deltaTime);

            return m_IsUsingJetpack;
        }

        public bool IsFlying => m_IsUsingJetpack;
    }
}
