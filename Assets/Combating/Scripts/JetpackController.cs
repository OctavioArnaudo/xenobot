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
        public float jetpackForce = 45f;      // Fuerza de impulso inicial
        public float hoverForce = 15f;        // Fuerza para mantener altura (compensar gravedad)
        public float fuelConsumption = 25f;   // Consumo por segundo
        public float fuelRegen = 20f;         // Regeneracion por segundo
        public float maxUpwardVelocity = 8f;  // Tope de velocidad de ascenso
        public float hoverThreshold = 0.5f;   // Umbral de velocidad para entrar en modo sobrevuelo

        private HealthController m_Health;
        private CharacterController m_CharController;
        private bool m_IsUsingJetpack = false;

        void Awake()
        {
            m_Health = GetComponent<HealthController>();
            m_CharController = GetComponent<CharacterController>();

            // Asegurar que el HealthController tenga capacidad de Jetpack para el HUD
            if (m_Health != null && m_Health.maxJetpack <= 0)
            {
                m_Health.maxJetpack = 100f;
                m_Health.AddFuel(100f);
            }
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
                // Regenerar fuel rapidamente en el suelo
                m_Health.AddFuel(fuelRegen * Time.deltaTime);
                return false;
            }

            // Lógica de Vuelo
            if (isJumpHeld && m_Health.JetpackFuel > 0)
            {
                m_IsUsingJetpack = true;

                // Si estamos cayendo, frenar la caida bruscamente (Air Brake)
                if (verticalVelocity < -2f)
                {
                    verticalVelocity = Mathf.Lerp(verticalVelocity, 0, Time.deltaTime * 10f);
                }

                // Determinar si estamos impulsando o sobrevolando
                // Si ya alcanzamos una velocidad vertical positiva, aplicamos menos fuerza para "sobrevolar"
                float currentForce = (verticalVelocity > hoverThreshold) ? hoverForce : jetpackForce;

                verticalVelocity += currentForce * Time.deltaTime;

                // Limitar velocidad maxima de ascenso
                if (verticalVelocity > maxUpwardVelocity)
                    verticalVelocity = maxUpwardVelocity;

                // Consumir fuel
                m_Health.UseFuel(fuelConsumption * Time.deltaTime);
            }
            else
            {
                // Regenerar fuel muy lentamente en el aire si no se usa
                m_Health.AddFuel((fuelRegen * 0.2f) * Time.deltaTime);
            }

            return m_IsUsingJetpack;
        }

        public bool IsFlying => m_IsUsingJetpack;
    }
}
