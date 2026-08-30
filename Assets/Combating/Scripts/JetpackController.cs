using UnityEngine;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for jetpack flight logic.
    /// Communicates with HealthController for fuel management.
    /// </summary>
    [RequireComponent(typeof(HealthController))]
    public class JetpackController : NetworkBehaviour
    {
        [Header("Flight Settings")]
        public float jetpackForce = 60f;
        public float fuelConsumption = 20f;
        public float fuelRegen = 25f;
        public float maxUpwardVelocity = 15f;

        private HealthController m_Health;
        private CharacterController m_CharController;
        private bool m_IsUsingJetpack = false;

        void Awake()
        {
            m_Health = GetComponent<HealthController>();
            m_CharController = GetComponent<CharacterController>();
        }

        /// <summary>
        /// Logic to be called from the movement controller.
        /// Handles the vertical lift and fuel consumption.
        /// </summary>
        public bool ProcessFlight(bool isJumpHeld, bool isGrounded, ref float verticalVelocity)
        {
            m_IsUsingJetpack = false;

            if (isGrounded)
            {
                // Regain fuel when on ground
                m_Health.AddFuel(fuelRegen * Time.deltaTime);
                return false;
            }

            // Flight Logic
            if (isJumpHeld && m_Health.JetpackFuel > 0)
            {
                m_IsUsingJetpack = true;

                // Neutralize gravity fall if just started flying
                if (verticalVelocity < 0) verticalVelocity = 0;

                // Apply lift
                verticalVelocity += jetpackForce * Time.deltaTime;

                // Sustain/Hover limit
                if (verticalVelocity > maxUpwardVelocity)
                    verticalVelocity = maxUpwardVelocity;

                // Consume fuel
                m_Health.UseFuel(fuelConsumption * Time.deltaTime);
            }
            else
            {
                // Regain fuel in mid-air (slower) if not using it
                m_Health.AddFuel((fuelRegen * 0.5f) * Time.deltaTime);
            }

            return m_IsUsingJetpack;
        }

        public bool IsFlying => m_IsUsingJetpack;
    }
}
