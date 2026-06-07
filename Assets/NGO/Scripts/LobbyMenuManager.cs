using Unity.Netcode;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Collections;
using Unity.Netcode.Transports.UTP;

namespace NGO.Networking
{
    public class LobbyMenuManager : NetworkBehaviour
    {
        [Header("UI Referencias (Botones)")]
        [SerializeField] private Button timerButton;
        [SerializeField] private Button counterButton;
        [SerializeField] private Button portButton;
        [SerializeField] private Button codeButton;
        [SerializeField] private GameObject roomCanvas;

        [Header("Configuración")]
        [SerializeField] private float lobbyDuration = 300f;
        [SerializeField] private string biomaSceneName = "BiomaScene";
        [SerializeField] private string networkSceneName = "NetworkScene";

        private NetworkVariable<float> m_TimeRemaining = new NetworkVariable<float>(300f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<int> m_MaxPlayers = new NetworkVariable<int>(4, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<FixedString32Bytes> m_JoinCode = new NetworkVariable<FixedString32Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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

        public override void OnNetworkSpawn()
        {
            Debug.Log($"[Lobby] Jugador entró al lobby. Servidor: {IsServer}");

            if (IsServer)
            {
                m_TimeRemaining.Value = lobbyDuration;
                m_MaxPlayers.Value = LocalUserConfig.MaxPlayers;
                m_JoinCode.Value = LocalUserConfig.LastJoinCode;
            }

            if (roomCanvas != null) roomCanvas.SetActive(false);

            UpdateConnectionInfo();
        }

        private void UpdateConnectionInfo()
        {
            // IP y Puerto (Solo visible si no es Relay, o como info general)
            if (m_PortText != null)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    string ip = transport.ConnectionData.Address;
                    ushort port = transport.ConnectionData.Port;
                    m_PortText.text = $"{ip}:{port}";
                }
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
        }

        public void OnClickStartGame()
        {
            if (IsServer) NetworkManager.Singleton.SceneManager.LoadScene(biomaSceneName, LoadSceneMode.Single);
        }

        public void OnClickLeaveLobby()
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene(networkSceneName);
        }
    }
}
