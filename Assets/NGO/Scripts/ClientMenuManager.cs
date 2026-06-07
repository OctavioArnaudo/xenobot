using UnityEngine;
using TMPro;
using Unity.Netcode;

namespace NGO.Networking
{
    /// <summary>
    /// Manager específico para el Canvas de Cliente.
    /// Maneja la entrada de IP, Puerto y Código de Sala.
    /// </summary>
    public class ClientMenuManager : MonoBehaviour
    {
        [Header("UI Referencias - Identidad")]
        [SerializeField] private TMP_InputField nameInputField;
        [SerializeField] private TMP_InputField customIdInputField;
        [SerializeField] private UnityEngine.UI.Image colorDisplay;

        [Header("UI Referencias - Conexión")]
        [SerializeField] private TMP_InputField ipInputField;
        [SerializeField] private TMP_InputField portInputField;
        [SerializeField] private TMP_InputField roomCodeInputField;

        [Header("Componentes Modulares")]
        [SerializeField] private NetworkAddressConfigurator addressConfigurator;

        private void Start()
        {
            // Valores por defecto
            if (nameInputField != null) nameInputField.text = "Player_" + Random.Range(10, 99);
            if (customIdInputField != null) customIdInputField.text = Random.Range(1000, 9999).ToString();

            if (ipInputField != null && string.IsNullOrEmpty(ipInputField.text))
                ipInputField.text = "127.0.0.1";

            if (portInputField != null && string.IsNullOrEmpty(portInputField.text))
                portInputField.text = "7777";
        }

        private void SaveLocalSettings()
        {
            if (nameInputField != null) LocalUserConfig.UserName = nameInputField.text;
            if (customIdInputField != null && int.TryParse(customIdInputField.text, out int id))
                LocalUserConfig.UserCustomID = id;
            if (colorDisplay != null) LocalUserConfig.UserColor = colorDisplay.color;
        }

        /// <summary>
        /// Se conecta a través de IP y Puerto.
        /// </summary>
        public void OnClickConnectByIP()
        {
            SaveLocalSettings();

            string ip = ipInputField != null ? ipInputField.text : "127.0.0.1";
            ushort port = 7777;

            if (portInputField != null && ushort.TryParse(portInputField.text, out ushort parsedPort))
            {
                port = parsedPort;
            }

            if (addressConfigurator != null)
            {
                addressConfigurator.SetIPAddress(ip, port);
            }

            Debug.Log($"[ClientMenu] Conectando a {ip}:{port}...");
            NetworkingService.StartClient(ip, port);
        }

        /// <summary>
        /// Se conecta usando un Código de Sala (Unity Relay).
        /// </summary>
        public async void OnClickConnectByRoomCode()
        {
            SaveLocalSettings();

            string code = roomCodeInputField != null ? roomCodeInputField.text : "";
            if (string.IsNullOrEmpty(code))
            {
                Debug.LogWarning("[ClientMenu] El código de sala está vacío.");
                return;
            }

            Debug.Log($"[ClientMenu] Intentando conectar mediante código: {code}");

            // Intentar unirse vía Relay
            bool success = await RelayManager.JoinRelay(code);

            if (success)
            {
                if (NetworkManager.Singleton.StartClient())
                {
                    Debug.Log("[ClientMenu] Conectado exitosamente vía Relay.");
                }
            }
            else
            {
                Debug.LogError("[ClientMenu] No se pudo unir a la sala. Verifica que el código sea correcto.");
            }
        }
    }
}
