using Unity.Netcode;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Collections;
using Unity.Netcode.Transports.UTP;
using System.Net.Http;
using System.Threading.Tasks;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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

        private bool m_IsManuallyOpened = false;

        private void Update()
        {
            if (!IsSpawned || NetworkManager.Singleton == null || (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer))
            {
                if (m_TimerText != null) m_TimerText.text = "OFFLINE";
                if (m_PlayersText != null) m_PlayersText.text = "--/--";
                return;
            }

            // Abrir/Cerrar menú localmente con la tecla Escape
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                m_IsManuallyOpened = !m_IsManuallyOpened;
                ToggleRoomCanvasLocal(m_IsManuallyOpened);
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                m_IsManuallyOpened = !m_IsManuallyOpened;
                ToggleRoomCanvasLocal(m_IsManuallyOpened);
            }
#endif

            if (IsServer)
            {
                if (m_TimeRemaining.Value > 0) m_TimeRemaining.Value -= Time.deltaTime;
                CheckStartConditions();
            }

            UpdateUI();
        }

        /// <summary>
        /// Permite abrir o cerrar el panel de la sala localmente para acceder a los botones.
        /// </summary>
        private void ToggleRoomCanvasLocal(bool show)
        {
            if (roomCanvas != null)
            {
                roomCanvas.SetActive(show);

                if (show)
                {
                    roomCanvas.transform.SetAsLastSibling();

                    // El Host SIEMPRE puede ver el botón de inicio si abre el menú manualmente
                    if (startGameButton != null)
                    {
                        startGameButton.gameObject.SetActive(IsServer);
                        startGameButton.interactable = IsServer;
                    }

                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
        }

        private void CheckStartConditions()
        {
            int currentPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;
            bool timerExpired = m_TimeRemaining.Value <= 0;
            bool isFull = currentPlayers >= m_MaxPlayers.Value;

            // Auto-Mostrar: Si el tiempo acabó o está lleno
            if (timerExpired || isFull)
            {
                if (roomCanvas != null && !roomCanvas.activeSelf) ShowRoomCanvasRpc();
            }
            else
            {
                // Auto-Ocultar: Solo si el tiempo NO ha acabado y ya no está lleno
                // Pero el Host puede mantenerlo abierto localmente si lo abrió con Esc
                if (roomCanvas != null && roomCanvas.activeSelf && !m_IsManuallyOpened)
                {
                    HideRoomCanvasRpc();
                }
            }
        }

        [Rpc(SendTo.Everyone)]
        private void ShowRoomCanvasRpc()
        {
            if (roomCanvas != null)
            {
                roomCanvas.SetActive(true);
                roomCanvas.transform.SetAsLastSibling();

                // Asegurar interactividad con CanvasGroup
                CanvasGroup cg = roomCanvas.GetComponent<CanvasGroup>();
                if (cg == null) cg = roomCanvas.AddComponent<CanvasGroup>();
                cg.interactable = true;
                cg.blocksRaycasts = true;

                // Solo el Host ve y puede pulsar el botón de inicio
                if (startGameButton != null)
                {
                    startGameButton.gameObject.SetActive(IsServer);
                    startGameButton.interactable = IsServer;
                }

                // Habilitar el mouse
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Debug.Log($"[Lobby] Room Canvas activo. Servidor: {IsServer}");
            }
        }

        [Rpc(SendTo.Everyone)]
        private void HideRoomCanvasRpc()
        {
            if (roomCanvas != null)
            {
                roomCanvas.SetActive(false);
                Debug.Log("[Lobby] Room Canvas desactivado por falta de jugadores.");
            }
        }

        private void UpdateUI()
        {
            // Mantener el cursor libre si el panel está activo
            if (roomCanvas != null && roomCanvas.activeSelf)
            {
                if (Cursor.lockState != CursorLockMode.None)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
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
            Debug.Log("[Lobby] Saliendo de la sesión y regresando al menú...");

            // Detener y destruir TODOS los NetworkManagers para una limpieza total
            var allNMs = Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
            foreach (var nm in allNMs)
            {
                if (nm.IsListening) nm.Shutdown();
                Destroy(nm.gameObject);
            }

            // 2. Aseguramos que el cursor sea visible y libre para interactuar con el menú principal
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // 3. Limpieza de seguridad: Si el jugador era persistente (DontDestroyOnLoad),
            // lo buscamos y destruimos para que no aparezca en el menú principal.
            var localPlayers = GameObject.FindGameObjectsWithTag("Player");
            foreach (var p in localPlayers)
            {
                // Solo destruimos los que no pertenezcan a la escena actual (que son los persistentes)
                if (p.scene.name == null || p.scene.name == "DontDestroyOnLoad")
                {
                    Destroy(p);
                }
            }

            // 4. Cargamos la escena inicial
            if (!string.IsNullOrEmpty(networkSceneName))
            {
                SceneManager.LoadScene(networkSceneName);
            }
            else
            {
                Debug.LogWarning("[Lobby] networkSceneName no está configurado. Intentando cargar 'NetworkScene' por defecto.");
                SceneManager.LoadScene("NetworkScene");
            }
        }
    }
}
