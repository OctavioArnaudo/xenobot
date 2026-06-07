using Unity.Netcode;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NGO.Networking
{
    public class LobbyMenuManager : NetworkBehaviour
    {
        [Header("UI Referencias (Botones)")]
        [SerializeField] private Button timerButton;
        [SerializeField] private Button playersCountButton;
        [SerializeField] private GameObject roomCanvas;

        [Header("Configuración")]
        [SerializeField] private float lobbyDuration = 300f;
        [SerializeField] private string biomaSceneName = "BiomaScene";
        [SerializeField] private string networkSceneName = "NetworkScene";

        private NetworkVariable<float> m_TimeRemaining = new NetworkVariable<float>(300f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<int> m_MaxPlayers = new NetworkVariable<int>(4, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private TMP_Text m_TimerText;
        private TMP_Text m_PlayersText;

        private void Awake()
        {
            FindTextComponents();
        }

        private void FindTextComponents()
        {
            if (timerButton != null) m_TimerText = timerButton.GetComponentInChildren<TMP_Text>();
            if (playersCountButton != null) m_PlayersText = playersCountButton.GetComponentInChildren<TMP_Text>();
        }

        public override void OnNetworkSpawn()
        {
            Debug.Log($"[Lobby] Jugador entró al lobby. Servidor: {IsServer}");

            if (IsServer)
            {
                m_TimeRemaining.Value = lobbyDuration;
                m_MaxPlayers.Value = LocalUserConfig.MaxPlayers;
            }

            if (roomCanvas != null) roomCanvas.SetActive(false);
        }

        private void Update()
        {
            // Si no hay red, mostramos estado de desconexión
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
