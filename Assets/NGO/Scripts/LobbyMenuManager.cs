using Unity.Netcode;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NGO.Networking
{
    /// <summary>
    /// Manager para la escena de Lobby.
    /// Sincroniza el temporizador y el conteo de jugadores antes de iniciar la partida.
    /// </summary>
    public class LobbyMenuManager : NetworkBehaviour
    {
        [Header("UI Referencias (Botones)")]
        [SerializeField] private Button timerButton;
        [SerializeField] private Button playersCountButton;
        [SerializeField] private GameObject roomCanvas;

        [Header("Configuración")]
        [SerializeField] private float lobbyDuration = 300f; // 5 minutos
        [SerializeField] private string biomaSceneName = "BiomaScene";
        [SerializeField] private string networkSceneName = "NetworkScene";

        // Variables de red sincronizadas
        private NetworkVariable<float> m_TimeRemaining = new NetworkVariable<float>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<int> m_MaxPlayers = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private TMP_Text m_TimerText;
        private TMP_Text m_PlayersText;

        private void Awake()
        {
            // Buscamos los componentes de texto dentro de los botones
            if (timerButton != null) m_TimerText = timerButton.GetComponentInChildren<TMP_Text>();
            if (playersCountButton != null) m_PlayersText = playersCountButton.GetComponentInChildren<TMP_Text>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                m_TimeRemaining.Value = lobbyDuration;
                m_MaxPlayers.Value = LocalUserConfig.MaxPlayers;
            }

            if (roomCanvas != null) roomCanvas.SetActive(false);
        }

        private void Update()
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                UpdateServerTimer();
                CheckStartConditions();
            }

            UpdateUI();
        }

        private void UpdateServerTimer()
        {
            if (m_TimeRemaining.Value > 0)
            {
                m_TimeRemaining.Value -= Time.deltaTime;
            }
        }

        private void CheckStartConditions()
        {
            int currentPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;

            // Condición: Tiempo agotado o lobby lleno
            if (m_TimeRemaining.Value <= 0 || currentPlayers >= m_MaxPlayers.Value)
            {
                if (roomCanvas != null && !roomCanvas.activeSelf)
                {
                    ShowRoomCanvasRpc();
                }
            }
        }

        [Rpc(SendTo.Everyone)]
        private void ShowRoomCanvasRpc()
        {
            if (roomCanvas != null) roomCanvas.SetActive(true);
        }

        private void UpdateUI()
        {
            // Actualizar Temporizador (MM:SS) en el botón
            if (m_TimerText != null)
            {
                float t = Mathf.Max(0, m_TimeRemaining.Value);
                int minutes = Mathf.FloorToInt(t / 60);
                int seconds = Mathf.FloorToInt(t % 60);
                m_TimerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            }

            // Actualizar Jugadores (00/00) en el botón
            if (m_PlayersText != null)
            {
                int current = NetworkManager.Singleton.ConnectedClientsIds.Count;
                int max = m_MaxPlayers.Value;
                m_PlayersText.text = string.Format("{0:00}/{1:00}", current, max);
            }
        }

        public void OnClickStartGame()
        {
            if (!IsServer) return;
            NetworkManager.Singleton.SceneManager.LoadScene(biomaSceneName, LoadSceneMode.Single);
        }

        public void OnClickLeaveLobby()
        {
            NetworkManager.Singleton.Shutdown();
            SceneManager.LoadScene(networkSceneName);
        }
    }
}
