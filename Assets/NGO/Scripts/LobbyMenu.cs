using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using Unity.Collections;
using Unity.Netcode.Transports.UTP;
using System.Net;
using System.Net.Sockets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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
        private GameObject _centerPanel;
        private TMP_Text _timerText;
        private TMP_Text _playersText;
        private TMP_Text _ipPortText;
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
                m_MaxPlayers.Value = PlayerPrefs.GetInt(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.MaxPlayers), 1);

                string code = PlayerPrefs.GetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.LastJoinCode), "");
                if (string.IsNullOrEmpty(code))
                {
                    code = "XENO" + Random.Range(10, 99);
                    PlayerPrefs.SetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.LastJoinCode), code);
                    PlayerPrefs.Save();
                }
                m_JoinCode.Value = code.ToUpper();
                NetworkMenu.AddActiveCode(code.ToUpper());
            }

            BuildCyberpunkUI();
        }

        private void Update()
        {
            bool escapePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!escapePressed) escapePressed = Input.GetKeyDown(KeyCode.Escape);
#endif

            if (escapePressed && _centerPanel != null)
            {
                bool show = !_centerPanel.activeSelf;
                _centerPanel.SetActive(show);
                Cursor.visible = show;
                Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
            }

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
                _timerText.text = string.Format("{0:00}:{1:00}", Mathf.FloorToInt(t / 60), Mathf.FloorToInt(t % 60));
            }

            if (_playersText != null)
            {
                _playersText.text = string.Format("{0:00}/{1:00}", NetworkManager.Singleton.ConnectedClientsIds.Count, m_MaxPlayers.Value);
            }

            if (_codeText != null)
            {
                string code = m_JoinCode.Value.ToString();
                _codeText.text = string.IsNullOrEmpty(code) ? "DIRECT IP" : code;
            }

            if (_ipPortText != null)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                ushort port = transport != null ? transport.ConnectionData.Port : (ushort)7777;
                _ipPortText.text = $"{GetLocalIPAddress()}:{port}";
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
            string currentCode = m_JoinCode.Value.ToString();
            if (!string.IsNullOrEmpty(currentCode))
            {
                NetworkMenu.RemoveActiveCode(currentCode);
            }

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

        private string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
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

            // 1. CORNER HUD ELEMENTS (ALWAYS VISIBLE - OUTSIDE LABEL + INSIDE VALUE BOX)
            // Top-Left: Timer
            _timerText = CreateCornerHudBox("TimerHud", _canvasRoot.transform, "TIMER:", "05:00", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -30), neonCyan);

            // Top-Right: Players
            _playersText = CreateCornerHudBox("PlayersHud", _canvasRoot.transform, "PLAYERS:", "01/04", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -30), neonYellow);

            // Bottom-Left: IP:Port
            _ipPortText = CreateCornerHudBox("IpPortHud", _canvasRoot.transform, "IP:PORT", "127.0.0.1:7777", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(30, 30), neonCyan);

            // Bottom-Right: Code
            _codeText = CreateCornerHudBox("CodeHud", _canvasRoot.transform, "CODE:", "READY", new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 30), neonMagenta);

            // 2. CENTER PANEL (TOGGLED VIA ESC KEY)
            _centerPanel = new GameObject("CenterPanel", typeof(RectTransform));
            _centerPanel.transform.SetParent(_canvasRoot.transform, false);
            var pImg = _centerPanel.AddComponent<Image>();
            pImg.color = panelColor;
            var pRt = _centerPanel.GetComponent<RectTransform>();
            pRt.sizeDelta = new Vector2(750, 520);
            var outline = _centerPanel.AddComponent<Outline>();
            outline.effectColor = neonCyan;
            outline.effectDistance = new Vector2(3, 3);

            // Title
            GameObject title = new GameObject("Title", typeof(RectTransform));
            title.transform.SetParent(_centerPanel.transform, false);
            var tTxt = title.AddComponent<TextMeshProUGUI>();
            tTxt.text = "LOBBY";
            tTxt.fontSize = 54;
            tTxt.alignment = TextAlignmentOptions.Center;
            tTxt.fontStyle = FontStyles.Bold;
            tTxt.color = Color.white;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchoredPosition = new Vector2(0, 170);
            tRt.sizeDelta = new Vector2(650, 70);

            // Subtitle
            GameObject sub = new GameObject("Subtitle", typeof(RectTransform));
            sub.transform.SetParent(_centerPanel.transform, false);
            var sTxt = sub.AddComponent<TextMeshProUGUI>();
            sTxt.text = "// WAITROOM PREPARATION";
            sTxt.fontSize = 20;
            sTxt.alignment = TextAlignmentOptions.Center;
            sTxt.color = neonCyan;
            var sRt = sub.GetComponent<RectTransform>();
            sRt.anchoredPosition = new Vector2(0, 115);
            sRt.sizeDelta = new Vector2(650, 35);

            // Action Buttons
            _startButton = CreateCyberButton("StartMatchButton", _centerPanel.transform, "START", new Vector2(0, 10), neonCyan, OnClickStartMatch);
            CreateCyberButton("LeaveButton", _centerPanel.transform, "LEAVE", new Vector2(0, -75), neonMagenta, OnClickLeaveLobby);

            // Initially show center panel
            _centerPanel.SetActive(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private TMP_Text CreateCornerHudBox(string name, Transform parent, string labelText, string defaultValue, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Color accent)
        {
            GameObject container = new GameObject(name, typeof(RectTransform));
            container.transform.SetParent(parent, false);
            var cRt = container.GetComponent<RectTransform>();
            cRt.anchorMin = anchorMin;
            cRt.anchorMax = anchorMax;
            cRt.pivot = pivot;
            cRt.anchoredPosition = pos;
            cRt.sizeDelta = new Vector2(320, 85);

            // Variable Name Label OUTSIDE Box (Above)
            GameObject lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(container.transform, false);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text = labelText;
            lbl.fontSize = 22;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color = accent;
            var lRt = lblGo.GetComponent<RectTransform>();
            lRt.anchoredPosition = new Vector2(0, 25);
            lRt.sizeDelta = new Vector2(320, 30);

            // Box Panel INSIDE Container
            GameObject box = new GameObject("Box", typeof(RectTransform));
            box.transform.SetParent(container.transform, false);
            var img = box.AddComponent<Image>();
            img.color = new Color(0.04f, 0.08f, 0.12f, 0.90f);
            var bRt = box.GetComponent<RectTransform>();
            bRt.anchoredPosition = new Vector2(0, -15);
            bRt.sizeDelta = new Vector2(320, 50);
            var outline = box.AddComponent<Outline>();
            outline.effectColor = accent;

            // Value Text INSIDE Box
            GameObject txtGo = new GameObject("ValueText", typeof(RectTransform));
            txtGo.transform.SetParent(box.transform, false);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = defaultValue;
            txt.fontSize = 28;
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
            bRt.sizeDelta = new Vector2(400, 55);
            var outline = btn.AddComponent<Outline>();
            outline.effectColor = accent;
            outline.effectDistance = new Vector2(2, 2);

            GameObject txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(btn.transform, false);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 22;
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