using Unity.Netcode;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Collections;
using Unity.Netcode.Transports.UTP;
using System.Net.Http;
using System.Threading.Tasks;

namespace NGO.Networking
{
    public class LobbyMenuManager : NetworkBehaviour
    {
        [Header("UI Referencias (Botones)")]
        [SerializeField] private Button timerButton;
        [SerializeField] private Button counterButton;
        [SerializeField] private Button portButton;
        [SerializeField] private Button codeButton;
        [SerializeField] private Button startGameButton; // Botón para iniciar BiomaScene
        [SerializeField] private GameObject roomCanvas;

        [Header("Configuración")]
        [SerializeField] private float lobbyDuration = 300f;
        [SerializeField] private string biomaSceneName = "BiomaScene";
        [SerializeField] private string networkSceneName = "NetworkScene";

        private NetworkVariable<float> m_TimeRemaining = new NetworkVariable<float>(300f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<int> m_MaxPlayers = new NetworkVariable<int>(4, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<FixedString32Bytes> m_JoinCode = new NetworkVariable<FixedString32Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<FixedString32Bytes> m_ExternalIP = new NetworkVariable<FixedString32Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private TMP_Text m_TimerText;
        private TMP_Text m_PlayersText;
        private TMP_Text m_PortText;
        private TMP_Text m_CodeText;

        private void Awake()
        {
            FindTextComponents();
        }

        private void FindTextComponents()
        {
            if (timerButton != null) m_TimerText = timerButton.GetComponentInChildren<TMP_Text>();
            if (counterButton != null) m_PlayersText = counterButton.GetComponentInChildren<TMP_Text>();
            if (portButton != null) m_PortText = portButton.GetComponentInChildren<TMP_Text>();
            if (codeButton != null) m_CodeText = codeButton.GetComponentInChildren<TMP_Text>();
        }

        public override async void OnNetworkSpawn()
        {
            Debug.Log($"[Lobby] Jugador entró al lobby. Servidor: {IsServer}");

            if (IsServer)
            {
                m_TimeRemaining.Value = lobbyDuration;
                m_MaxPlayers.Value = LocalUserConfig.MaxPlayers;
                m_JoinCode.Value = LocalUserConfig.LastJoinCode.ToUpper();
                m_ExternalIP.Value = await GetExternalIPAddress();
            }

            if (roomCanvas != null) roomCanvas.SetActive(false);

            // Solo el servidor puede ver/interactuar con el botón de inicio
            if (startGameButton != null)
            {
                startGameButton.interactable = IsServer;
            }
        }

        private async Task<string> GetExternalIPAddress()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    return await client.GetStringAsync("https://api.ipify.org");
                }
            }
            catch
            {
                return "Unknown IP";
            }
        }

        private void Update()
        {
            if (!IsSpawned || NetworkManager.Singleton == null || (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer))
            {
                if (m_TimerText != null) m_TimerText.text = "OFFLINE";
                if (m_PlayersText != null) m_PlayersText.text = "--/--";
                return;
            }

            if (IsServer)
            {
                if (m_TimeRemaining.Value > 0) m_TimeRemaining.Value -= Time.deltaTime;
                CheckStartConditions();
            }

            UpdateUI();
        }

        private void CheckStartConditions()
        {
            int currentPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;
            if (m_TimeRemaining.Value <= 0 || currentPlayers >= m_MaxPlayers.Value)
            {
                if (roomCanvas != null && !roomCanvas.activeSelf) ShowRoomCanvasRpc();
            }
        }

        [Rpc(SendTo.Everyone)]
        private void ShowRoomCanvasRpc()
        {
            if (roomCanvas != null) roomCanvas.SetActive(true);
        }

        private void UpdateUI()
        {
            if (m_TimerText != null)
            {
                float t = Mathf.Max(0, m_TimeRemaining.Value);
                m_TimerText.text = string.Format("{0:00}:{1:00}", Mathf.FloorToInt(t / 60), Mathf.FloorToInt(t % 60));
            }

            if (m_PlayersText != null)
            {
                m_PlayersText.text = string.Format("{0:00}/{1:00}", NetworkManager.Singleton.ConnectedClientsIds.Count, m_MaxPlayers.Value);
            }

            if (m_CodeText != null)
            {
                string code = m_JoinCode.Value.ToString();
                m_CodeText.text = string.IsNullOrEmpty(code) ? "DIRECT IP" : code;
            }

            if (m_PortText != null)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    string extIp = m_ExternalIP.Value.ToString();
                    string locIp = GetLocalIPAddress();
                    ushort port = transport.ConnectionData.Port;

                    // Priorizamos la IP externa si está disponible, si no mostramos la local.
                    string ipToShow = (string.IsNullOrEmpty(extIp) || extIp == "Unknown IP") ? locIp : extIp;
                    m_PortText.text = $"{ipToShow}:{port}";
                }
            }
        }

        private string GetLocalIPAddress()
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return ip.ToString();
            }
            return "127.0.0.1";
        }

        public void OnClickStartGame()
        {
            if (!IsServer)
            {
                Debug.LogWarning("[Lobby] Solo el servidor/host puede iniciar la partida.");
                return;
            }

            if (string.IsNullOrEmpty(biomaSceneName))
            {
                Debug.LogError("[Lobby] El nombre de la escena BiomaScene no está configurado en el Inspector.");
                return;
            }

            Debug.Log($"[Lobby] Host solicitando cambio a escena: {biomaSceneName}");

            var status = NetworkManager.Singleton.SceneManager.LoadScene(biomaSceneName, LoadSceneMode.Single);

            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[Lobby] Error al intentar cargar la escena {biomaSceneName}: {status}");
            }
        }

        public void OnClickLeaveLobby()
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene(networkSceneName);
        }
    }
}
