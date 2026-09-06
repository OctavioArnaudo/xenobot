using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Missions.Scripts
{
    /// <summary>
    /// Componente para activar o completar misiones.
    /// Funciona en Local y Multiplayer.
    /// </summary>
    [AddComponentMenu("Missions/Missions Component")]
    public class MissionController : MonoBehaviour
    {
        public enum TriggerMode { StartMission, CompleteMission }

        [Header("Override Message (Optional)")]
        public string customTitle;
        [TextArea] public string customDescription;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            var networkObj = other.GetComponent<NetworkObject>();
            bool isLocalPlayer = (networkObj == null) || networkObj.IsLocalPlayer;

            if (isLocalPlayer)
            {
                if (MissionsManager.Instance != null)
                {
                    // If we have a custom message, show it
                    if (!string.IsNullOrEmpty(customTitle))
                    {
                        MissionsManager.Instance.ShowMessage(customTitle, customDescription);
                    }

                    // Force a flow update
                    // MissionsManager will automatically determine the next mission based on inventory
                }
            }
        }
    }
}
