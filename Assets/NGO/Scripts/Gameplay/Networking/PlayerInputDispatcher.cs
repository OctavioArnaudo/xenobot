using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

namespace NGO.Gameplay.Networking
{
    /// <summary>
    /// Ubicado en el objeto "SystemsHub".
    /// Escucha el input del jugador local usando el NUEVO Input System
    /// y lo comunica al Hub en el mismo objeto.
    /// </summary>
    public class PlayerInputDispatcher : NetworkBehaviour
    {
        private PlayerSystemHub m_Hub;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            m_Hub = GetComponent<PlayerSystemHub>();
        }

        private void Update()
        {
            if (!IsOwner) return;

            // Click Izquierdo (Acción) -> Usando Mouse.current
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                m_Hub.TriggerAction();
            }

            // Tecla H (Consulta Datos) -> Ejemplo de cómo consultar un controlador de datos
            if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame)
            {
                Debug.Log("[Input] Consultando datos del Hub...");
                // Aquí podrías buscar un controlador de vida si existiera:
                // var health = m_Hub.GetModule<ManageHealthController>();
            }
        }
    }
}