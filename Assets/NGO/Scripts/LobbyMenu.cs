using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using Unity.Collections;
using Unity.Netcode.Transports.UTP;

namespace NGO.Networking
{
    public class LobbyMenu : NetworkBehaviour
    {
        public static LobbyMenu Instance { get; private set; }

        [Header("Scene Navigation")]
        [SerializeField] private string biomaSceneName = "BiomaScene";
        [SerializeField] private string networkSceneName = "NetworkScene";

        [Header("Cyberpunk Theme Colors")]
        public Color panelColor = new Color(0.04f, 0.08f, 0.12f, 0.92f);
        public Color neonCyan = new Color(0f, 1f, 0.95f, 1f);
        public Color neonMagenta = new Color(1f, 0f, 0.45f, 1f);
        public Color neonYellow = new Color(1f, 0.85f, 0f, 1f);

        private NetworkVariable<float> m_TimeRemaining = new NetworkVariable<float>(300f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<int> m_MaxPlayers = new NetworkVariable<int>(4, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkVariable<FixedString32Bytes> m_JoinCode = new NetworkVariable<FixedString32Bytes>("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private GameObject _canvasRoot;
        private TMP_Text _timerText;
        private TMP_Text _playersText;
        private TMP_Text _codeText;
        private Button _startButton;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                m_TimeRemaining.Value = 300f;
                m_MaxPlayers.Value = LocalUserConfig.MaxPlayers;
                m_JoinCode.Value = LocalUserConfig.LastJoinCode.ToUpper();
            }

            BuildCyberpunkUI();
        }

        private void Update()
        {
            if (!IsSpawned || NetworkManager.Singleton == null) return;

            if (IsServer && m_TimeRemaining.Value > 0)
            {
                m_TimeRemaining.Value -= Time.deltaTime;
            }

            UpdateUI();
        }

        private void UpdateUI()
        {
            if (_timerText != null)
            {
                float t = Mathf.Max(0, m_TimeRemaining.Value);
                _timerText.text = string.Format("TIMER: {0:00}:{1:00}", Mathf.FloorToInt(t / 60), Mathf.FloorToInt(t % 60));
            }

            if (_playersText != null)
            {
                _playersText.text = string.Format("PLAYERS: {0:00}/{1:00}", NetworkManager.Singleton.ConnectedClientsIds.Count, m_MaxPlayers.Value);
            }

            if (_codeText != null)
            {
                string code = m_JoinCode.Value.ToString();
                _codeText.text = string.IsNullOrEmpty(code) ? "RELAY: DIRECT IP" : $"RELAY CODE: {code}";
            }

            if (_startButton != null)
            {
                _startButton.gameObject.SetActive(IsServer);
                _startButton.interactable = IsServer;
            }
        }

        public void OnClickStartMatch()
        {
            if (!IsServer) return;
            NetworkManager.Singleton.SceneManager.LoadScene(biomaSceneName, LoadSceneMode.Single);
        }

        public void OnClickLeaveLobby()
        {
            var allNMs = Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
            foreach (var nm in allNMs)
            {
                if (nm.IsListening) nm.Shutdown();
                Destroy(nm.gameObject);
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            SceneManager.LoadScene(!string.IsNullOrEmpty(networkSceneName) ? networkSceneName : "NetworkScene");
        }

        private void BuildCyberpunkUI()
        {
            if (_canvasRoot != null) Destroy(_canvasRoot);

            _canvasRoot = new GameObject("LobbyMenu_Canvas");
            Canvas canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1500;

            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _canvasRoot.AddComponent<GraphicRaycaster>();

            // Dark Panel
            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(_canvasRoot.transform, false);
            var pImg = panel.AddComponent<Image>();
            pImg.color = panelColor;
            var pRt = panel.GetComponent<RectTransform>();
            pRt.sizeDelta = new Vector2(800, 600);
            var outline = panel.AddComponent<Outline>();
            outline.effectColor = neonCyan;
            outline.effectDistance = new Vector2(3, 3);

            // Title
            GameObject title = new GameObject("Title", typeof(RectTransform));
            title.transform.SetParent(panel.transform, false);
            var tTxt = title.AddComponent<TextMeshProUGUI>();
            tTxt.text = "LOBBY WAITROOM";
            tTxt.fontSize = 60;
            tTxt.alignment = TextAlignmentOptions.Center;
            tTxt.fontStyle = FontStyles.Bold;
            tTxt.color = Color.white;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchoredPosition = new Vector2(0, 210);
            tRt.sizeDelta = new Vector2(700, 80);

            // Info Displays
            _timerText = CreateDisplayBox("TimerBox", panel.transform, "TIMER: 05:00", new Vector2(0, 120), neonCyan);
            _playersText = CreateDisplayBox("PlayersBox", panel.transform, "PLAYERS: 01/04", new Vector2(0, 40), neonYellow);
            _codeText = CreateDisplayBox("CodeBox", panel.transform, "RELAY CODE: READY", new Vector2(0, -40), neonMagenta);

            // Action Buttons
            _startButton = CreateCyberButton("StartMatchButton", panel.transform, "START MATCH NOW", new Vector2(0, -130), neonCyan, OnClickStartMatch);
            CreateCyberButton("LeaveButton", panel.transform, "LEAVE LOBBY", new Vector2(0, -210), neonMagenta, OnClickLeaveLobby);

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private TMP_Text CreateDisplayBox(string name, Transform parent, string defaultText, Vector2 pos, Color accent)
        {
            GameObject box = new GameObject(name, typeof(RectTransform));
            box.transform.SetParent(parent, false);
            var img = box.AddComponent<Image>();
            img.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
            var bRt = box.GetComponent<RectTransform>();
            bRt.anchoredPosition = pos;
            bRt.sizeDelta = new Vector2(550, 55);
            var outline = box.AddComponent<Outline>();
            outline.effectColor = accent;

            GameObject txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(box.transform, false);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = defaultText;
            txt.fontSize = 24;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;
            txt.color = Color.white;
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;

            return txt;
        }

        private Button CreateCyberButton(string name, Transform parent, string label, Vector2 pos, Color accent, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btn = new GameObject(name, typeof(RectTransform));
            btn.transform.SetParent(parent, false);
            var img = btn.AddComponent<Image>();
            img.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
            var bBtn = btn.AddComponent<Button>();
            bBtn.onClick.AddListener(onClick);
            var bRt = btn.GetComponent<RectTransform>();
            bRt.anchoredPosition = pos;
            bRt.sizeDelta = new Vector2(450, 60);
            var outline = btn.AddComponent<Outline>();
            outline.effectColor = accent;
            outline.effectDistance = new Vector2(2, 2);

            GameObject txtGo = new GameObject("Text");
            txtGo.transform.SetParent(btn.transform, false);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 24;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;
            txt.color = Color.white;
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;

            return bBtn;
        }
    }
}