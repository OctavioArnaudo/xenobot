using UnityEngine;
using Unity.Netcode;
using Missions.Manager;

namespace Missions.Game
{
    /// <summary>
    /// Componente para activar o completar misiones.
    /// Funciona en Local y Multiplayer.
    /// </summary>
    [AddComponentMenu("Missions/Missions Component")]
    public class MissionsGame : MonoBehaviour
    {
        public enum TriggerMode { StartMission, CompleteMission }

        [Header("Configuración")]
        public TriggerMode mode = TriggerMode.StartMission;
        public string targetNameOrId;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            // Verificamos si es el jugador local o si estamos en modo offline
            var networkObj = other.GetComponent<NetworkObject>();
            bool isLocalPlayer = (networkObj == null) || networkObj.IsLocalPlayer;

            if (isLocalPlayer)
            {
                if (MissionsManager.Instance == null)
                {
                    Debug.LogWarning("[MissionsComponent] No se encontró MissionsManager en la escena.");
                    return;
                }

                if (mode == TriggerMode.StartMission)
                {
                    MissionsManager.Instance.CheckLocation(targetNameOrId);
                }
                else
                {
                    // Usa el método híbrido del Manager
                    MissionsManager.Instance.CompleteMission(targetNameOrId);
                }
            }
        }
    }
}
