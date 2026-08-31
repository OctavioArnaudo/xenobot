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
        public float jetpackForce = 60f;      // Fuerza aumentada
        public float hoverForce = 25f;        // Mantener altura
        public float fuelConsumption = 30f;   // Consumo mas visible
        public float fuelRegen = 15f;         // Regeneracion equilibrada
        public float maxUpwardVelocity = 12f; // Limite de ascenso
        public float hoverThreshold = 0.5f;

        private HealthController m_Health;
        private CharacterController m_CharController;
        private bool m_IsUsingJetpack = false;
        private bool m_JetpackDepleted = false; // Flag to prevent stuttering when fuel hits zero

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
                // Reseteamos el estado de agotamiento y regeneramos fuel rápidamente en el suelo
                m_JetpackDepleted = false;
                m_Health.AddFuel(fuelRegen * Time.deltaTime);
                return false;
            }

            // Si soltamos el botón de salto, el jetpack deja de estar en estado "agotado"
            // permitiendo que se use de nuevo en cuanto tenga algo de fuel.
            if (!isJumpHeld)
            {
                m_JetpackDepleted = false;
            }

            // Si el fuel llega a cero, marcamos el jetpack como agotado.
            // Esto evita que el pequeño fuel regenerado en el aire lo active por micro-segundos (stuttering).
            if (m_Health.JetpackFuel <= 0)
            {
                m_JetpackDepleted = true;
            }

            // Lógica de Vuelo: Solo se activa si hay fuel, se mantiene el salto Y no está agotado
            if (isJumpHeld && !m_JetpackDepleted && m_Health.JetpackFuel > 0)
            {
                m_IsUsingJetpack = true;

                // Si estamos cayendo, frenar la caida bruscamente (Air Brake)
                if (verticalVelocity < -2f)
                {
                    verticalVelocity = Mathf.Lerp(verticalVelocity, 0, Time.deltaTime * 10f);
                }

                // Determinar si estamos impulsando o sobrevolando
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
                // Si no se está usando, se recarga automáticamente (muy lento en el aire)
                m_Health.AddFuel((fuelRegen * 0.2f) * Time.deltaTime);
            }

            return m_IsUsingJetpack;
        }

        public bool IsFlying => m_IsUsingJetpack;
    }
}
