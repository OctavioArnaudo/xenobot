using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Logic controller for jetpack flight.
    /// Handles fuel consumption and vertical movement.
    /// Modified to work as a modular component on the player.
    /// </summary>
    public class PropulsionController : MonoBehaviour, IItemFunctional
    {
        [Header("Flight Settings")]
        public float jetpackForce = 60f;
        public float hoverForce = 25f;
        public float fuelConsumption = 30f;
        public float fuelRegen = 15f;
        public float maxUpwardVelocity = 12f;
        public float hoverThreshold = 0.5f;

        private HealthController m_Health;
        private CharacterController m_CharController;
        private PlayerController m_Player;
        private bool m_IsUsingJetpack = false;
        private bool m_JetpackDepleted = false;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        void Awake()
        {
            RefreshReferences();
        }

        public void ApplyEffect(GameObject player)
        {
            m_Player = player.GetComponent<PlayerController>();
            m_Health = player.GetComponent<HealthController>();
            m_CharController = player.GetComponent<CharacterController>();

            if (m_Health != null && m_Health.maxJetpack <= 0)
            {
                m_Health.maxJetpack = 100f;
                m_Health.AddFuel(100f);
            }
            Debug.Log("[PropulsionController] Lógica de vuelo activada para el jugador.");
        }

        private void RefreshReferences()
        {
            if (m_Player == null) m_Player = GetComponentInParent<PlayerController>();
            if (m_Health == null) m_Health = GetComponentInParent<HealthController>();
            if (m_CharController == null) m_CharController = GetComponentInParent<CharacterController>();
        }

        public bool ProcessFlight(bool isJumpHeld, bool isGrounded, ref float verticalVelocity)
        {
            if (m_Health == null || m_Player == null) RefreshReferences();
            if (m_Health == null || m_Player == null) return false;

            // Only the owner processes flight logic (Safely check NetworkManager)
            bool isOwner = IsNetworkActive ? m_Player.IsOwner : true;
            if (!isOwner) return false;

            m_IsUsingJetpack = false;
            if (isGrounded)
            {
                m_JetpackDepleted = false;
                m_Health.AddFuel(fuelRegen * Time.deltaTime);
                return false;
            }

            if (!isJumpHeld) m_JetpackDepleted = false;
            if (m_Health.JetpackFuel <= 0) m_JetpackDepleted = true;

            if (isJumpHeld && !m_JetpackDepleted && m_Health.JetpackFuel > 0)
            {
                m_IsUsingJetpack = true;
                if (verticalVelocity < -2f) verticalVelocity = Mathf.Lerp(verticalVelocity, 0, Time.deltaTime * 10f);
                float currentForce = (verticalVelocity > hoverThreshold) ? hoverForce : jetpackForce;
                verticalVelocity += currentForce * Time.deltaTime;
                if (verticalVelocity > maxUpwardVelocity) verticalVelocity = maxUpwardVelocity;
                m_Health.UseFuel(fuelConsumption * Time.deltaTime);
            }
            else m_Health.AddFuel((fuelRegen * 0.2f) * Time.deltaTime);

            return m_IsUsingJetpack;
        }

        public bool IsFlying => m_IsUsingJetpack;
    }
}
