using UnityEngine;
using TMPro;

namespace NGO.Networking
{
    /// <summary>
    /// Manager específico para el Canvas de Host.
    /// Maneja la configuración inicial de la partida y visualización de códigos.
    /// </summary>
    public class HostMenuManager : MonoBehaviour
    {
        [Header("Configuración de Escena")]
        [SerializeField] private string targetScene = "BiomaScene";

        [Header("UI Referencias - Identidad")]
        [SerializeField] private TMP_InputField nameInputField;
        [SerializeField] private TMP_InputField customIdInputField;
        [SerializeField] private UnityEngine.UI.Image colorDisplay;

        [Header("UI Referencias - Sala")]
        [SerializeField] private TMP_Text roomCodeDisplay;
        [SerializeField] private TMP_InputField maxPlayersInput;

        [Header("Componentes Modulares")]
        [SerializeField] private NetworkSceneLoader sceneLoader;

        private void Start()
        {
            if (nameInputField != null) nameInputField.text = "Host_" + Random.Range(10, 99);
            if (customIdInputField != null) customIdInputField.text = "1";

            if (maxPlayersInput != null && string.IsNullOrEmpty(maxPlayersInput.text))
                maxPlayersInput.text = "4";
        }

        private void SaveLocalSettings()
        {
            if (nameInputField != null) LocalUserConfig.UserName = nameInputField.text;
            if (customIdInputField != null && int.TryParse(customIdInputField.text, out int id))
                LocalUserConfig.UserCustomID = id;
            if (colorDisplay != null) LocalUserConfig.UserColor = colorDisplay.color;
        }

        /// <summary>
        /// Inicia la sesión como Host usando Unity Relay (Genera código de sala).
        /// </summary>
        public async void OnClickStartHost()
        {
            SaveLocalSettings();

            int maxPlayers = 4;
            if (maxPlayersInput != null && int.TryParse(maxPlayersInput.text, out int parsedMax))
            {
                maxPlayers = parsedMax;
            }

            if (roomCodeDisplay != null) roomCodeDisplay.text = "CREATING...";

            // Intentar crear Relay
            string joinCode = await RelayManager.CreateRelay(maxPlayers);

            if (!string.IsNullOrEmpty(joinCode))
            {
                Debug.Log($"[HostMenu] Relay creado con código: {joinCode}");
                if (roomCodeDisplay != null) roomCodeDisplay.text = joinCode;

                if (NetworkingService.StartHost())
                {
                    if (sceneLoader != null)
                    {
                        sceneLoader.LoadScene(targetScene);
                    }
                }
            }
            else
            {
                Debug.LogError("[HostMenu] Fallo al crear la sala con Relay. Asegúrate de estar conectado a Internet y tener habilitado Unity Services.");
                if (roomCodeDisplay != null) roomCodeDisplay.text = "ERROR";
            }
        }

        /// <summary>
        /// Genera y muestra un código de sala (Preparado para Unity Relay).
        /// </summary>
        public void OnClickGenerateRoomCode()
        {
            // Lógica para obtener código de RelayService
            if (roomCodeDisplay != null) roomCodeDisplay.text = "WAITING...";
            Debug.Log("[HostMenu] Solicitando código de sala...");
        }
    }
}
