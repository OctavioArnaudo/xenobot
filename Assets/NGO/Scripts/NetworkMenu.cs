using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace NGO.Networking
{
    public class NetworkMenu : MonoBehaviour
    {
        public static NetworkMenu Instance { get; private set; }

        [Header("Scene Navigation")]
        [SerializeField] private string mainMenuScene = "MainMenuScene";
        [SerializeField] private string lobbySceneName = "LobbyScene";

        [Header("Cyberpunk Theme Colors")]
        public Color panelColor = new Color(0.04f, 0.08f, 0.12f, 0.92f);
        public Color neonCyan = new Color(0f, 1f, 0.95f, 1f);
        public Color neonMagenta = new Color(1f, 0f, 0.45f, 1f);
        public Color neonYellow = new Color(1f, 0.85f, 0f, 1f);

        private GameObject _canvasRoot;
        private GameObject _mainPanelGo;
        private GameObject _hostPanelGo;
        private GameObject _joinPanelGo;

        // Host Inputs & Displays
        private TMP_InputField _hostNameInput;
        private TMP_InputField _hostCustomIdInput;
        private TMP_InputField _hostMaxPlayersInput;
        private TMP_InputField _hostPortInput;
        private TMP_Text _hostCodeDisplay;

        // Join Inputs
        private TMP_InputField _joinNameInput;
        private TMP_InputField _joinCustomIdInput;
        private TMP_InputField _joinCodeInput;
        private TMP_InputField _joinIpInput;
        private TMP_InputField _joinPortInput;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Application.runInBackground = true;

            BuildCyberpunkUI();
            ShowMainSubpanel();
        }

        public void ShowMainSubpanel()
        {
            if (_mainPanelGo != null) _mainPanelGo.SetActive(true);
            if (_hostPanelGo != null) _hostPanelGo.SetActive(false);
            if (_joinPanelGo != null) _joinPanelGo.SetActive(false);
        }

        public void ShowHostSubpanel()
        {
            if (_mainPanelGo != null) _mainPanelGo.SetActive(false);
            if (_hostPanelGo != null) _hostPanelGo.SetActive(true);
            if (_joinPanelGo != null) _joinPanelGo.SetActive(false);
        }

        public void ShowJoinSubpanel()
        {
            if (_mainPanelGo != null) _mainPanelGo.SetActive(false);
            if (_hostPanelGo != null) _hostPanelGo.SetActive(false);
            if (_joinPanelGo != null) _joinPanelGo.SetActive(true);
        }

        public void GoBackToMainMenuScene()
        {
            SceneManager.LoadScene(mainMenuScene);
        }

        private void SaveHostSettings()
        {
            if (_hostNameInput != null) LocalUserConfig.UserName = _hostNameInput.text;
            if (_hostCustomIdInput != null && int.TryParse(_hostCustomIdInput.text, out int id))
                LocalUserConfig.UserCustomID = id;
            if (_hostMaxPlayersInput != null && int.TryParse(_hostMaxPlayersInput.text, out int max))
                LocalUserConfig.MaxPlayers = max;
        }

        private void SaveJoinSettings()
        {
            if (_joinNameInput != null) LocalUserConfig.UserName = _joinNameInput.text;
            if (_joinCustomIdInput != null && int.TryParse(_joinCustomIdInput.text, out int id))
                LocalUserConfig.UserCustomID = id;
        }

        public async void OnClickStartHostRelay()
        {
            SaveHostSettings();

            int maxPlayers = LocalUserConfig.MaxPlayers;
            if (_hostCodeDisplay != null) _hostCodeDisplay.text = "RELAY: CREATING...";

            string joinCode = await RelayManager.CreateRelay(maxPlayers);

            if (!string.IsNullOrEmpty(joinCode))
            {
                LocalUserConfig.LastJoinCode = joinCode;
                if (_hostCodeDisplay != null) _hostCodeDisplay.text = $"RELAY CODE: {joinCode}";

                if (NetworkingService.StartHost(isRelay: true))
                {
                    LoadLobbyScene();
                }
            }
            else
            {
                if (_hostCodeDisplay != null) _hostCodeDisplay.text = "RELAY ERROR";
            }
        }

        public void OnClickStartHostDirect()
        {
            SaveHostSettings();

            ushort port = 7777;
            if (_hostPortInput != null && ushort.TryParse(_hostPortInput.text, out ushort parsedPort)) port = parsedPort;

            if (NetworkingService.StartHost(port, isRelay: false))
            {
                LoadLobbyScene();
            }
        }

        public async void OnClickSmartConnectClient()
        {
            SaveJoinSettings();
            NetworkingService.Shutdown();

            string roomCode = _joinCodeInput != null ? _joinCodeInput.text.Trim().ToUpper() : "";
            string ip = _joinIpInput != null ? _joinIpInput.text.Trim() : "";

            if (!string.IsNullOrEmpty(roomCode))
            {
                bool success = await RelayManager.JoinRelay(roomCode);
                if (success)
                {
                    NetworkingService.StartClient(isRelay: true);
                }
            }
            else if (!string.IsNullOrEmpty(ip))
            {
                ushort port = 7777;
                if (_joinPortInput != null && ushort.TryParse(_joinPortInput.text, out ushort parsedPort)) port = parsedPort;

                SetUnityTransportAddress(ip, port);
                NetworkingService.StartClient(ip, port, isRelay: false);
            }
        }

        private void SetUnityTransportAddress(string ip, ushort port)
        {
            if (NetworkManager.Singleton != null)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    transport.SetConnectionData(ip, port);
                }
            }
        }

        private void LoadLobbyScene()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
            }
            else
            {
                SceneManager.LoadScene(lobbySceneName);
            }
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

            _canvasRoot = new GameObject("NetworkMenu_Canvas");
            Canvas canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _canvasRoot.AddComponent<GraphicRaycaster>();

            // Dark Translucent Background
            GameObject bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(_canvasRoot.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.01f, 0.02f, 0.05f, 0.90f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;

            // 1. MAIN PANEL
            _mainPanelGo = CreatePanel("MainPanel", _canvasRoot.transform, new Vector2(800, 650));
            CreateDecoration("[// NET_TERMINAL]", _mainPanelGo.transform, new Vector2(-260, 270), 22, neonCyan);
            CreateDecoration("[ONLINE_SERVICE]", _mainPanelGo.transform, new Vector2(260, 270), 22, neonMagenta);
            CreateHeader("MULTIPLAYER", "XENOBOT :: NETCODE PROTOCOL TERMINAL", _mainPanelGo.transform, new Vector2(0, 200));

            CreateCyberButton("HostMatchButton", _mainPanelGo.transform, "CREATE HOST MATCH", new Vector2(0, 40), neonCyan, ShowHostSubpanel);
            CreateCyberButton("JoinMatchButton", _mainPanelGo.transform, "JOIN ROOM MATCH", new Vector2(0, -50), neonYellow, ShowJoinSubpanel);
            CreateCyberButton("BackButton", _mainPanelGo.transform, "BACK TO MAIN MENU", new Vector2(0, -140), neonMagenta, GoBackToMainMenuScene);

            // 2. HOST SUBPANEL
            _hostPanelGo = CreatePanel("HostPanel", _canvasRoot.transform, new Vector2(850, 750));
            CreateHeader("HOST PROTOCOL", "// CREATE RELAY OR DIRECT IP MATCH", _hostPanelGo.transform, new Vector2(0, 260));

            _hostNameInput = CreateLabeledInput("HostNameInput", _hostPanelGo.transform, "OPERATOR NAME:", LocalUserConfig.UserName, new Vector2(0, 130));
            _hostCustomIdInput = CreateLabeledInput("HostCustomIdInput", _hostPanelGo.transform, "CUSTOM ID:", LocalUserConfig.UserCustomID.ToString(), new Vector2(0, 50));
            _hostMaxPlayersInput = CreateLabeledInput("HostMaxPlayersInput", _hostPanelGo.transform, "MAX PLAYERS:", LocalUserConfig.MaxPlayers.ToString(), new Vector2(0, -30));
            _hostPortInput = CreateLabeledInput("HostPortInput", _hostPanelGo.transform, "PORT:", "7777", new Vector2(0, -110));

            GameObject codeBox = new GameObject("CodeBox", typeof(RectTransform));
            codeBox.transform.SetParent(_hostPanelGo.transform, false);
            var cbImg = codeBox.AddComponent<Image>();
            cbImg.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
            var cbRt = codeBox.GetComponent<RectTransform>();
            cbRt.anchoredPosition = new Vector2(0, -180);
            cbRt.sizeDelta = new Vector2(550, 45);
            codeBox.AddComponent<Outline>().effectColor = neonYellow;

            GameObject cbTxtGo = new GameObject("CodeText", typeof(RectTransform));
            cbTxtGo.transform.SetParent(codeBox.transform, false);
            _hostCodeDisplay = cbTxtGo.AddComponent<TextMeshProUGUI>();
            _hostCodeDisplay.text = $"IP: {GetLocalIPAddress()}   |   RELAY: READY";
            _hostCodeDisplay.fontSize = 20;
            _hostCodeDisplay.alignment = TextAlignmentOptions.Center;
            _hostCodeDisplay.color = neonYellow;
            var cbTxtRt = cbTxtGo.GetComponent<RectTransform>();
            cbTxtRt.anchorMin = Vector2.zero;
            cbTxtRt.anchorMax = Vector2.one;

            CreateCyberButton("StartHostRelayBtn", _hostPanelGo.transform, "START HOST (RELAY)", new Vector2(-150, -255), neonCyan, OnClickStartHostRelay);
            CreateCyberButton("StartHostDirectBtn", _hostPanelGo.transform, "START DIRECT HOST", new Vector2(150, -255), neonYellow, OnClickStartHostDirect);
            CreateCyberButton("HostBackBtn", _hostPanelGo.transform, "BACK", new Vector2(0, -325), neonMagenta, ShowMainSubpanel);

            // 3. JOIN SUBPANEL
            _joinPanelGo = CreatePanel("JoinPanel", _canvasRoot.transform, new Vector2(850, 750));
            CreateHeader("JOIN PROTOCOL", "// CONNECT TO RELAY CODE OR DIRECT IP", _joinPanelGo.transform, new Vector2(0, 260));

            _joinNameInput = CreateLabeledInput("JoinNameInput", _joinPanelGo.transform, "OPERATOR NAME:", LocalUserConfig.UserName, new Vector2(0, 130));
            _joinCustomIdInput = CreateLabeledInput("JoinCustomIdInput", _joinPanelGo.transform, "CUSTOM ID:", LocalUserConfig.UserCustomID.ToString(), new Vector2(0, 50));
            _joinCodeInput = CreateLabeledInput("JoinCodeInput", _joinPanelGo.transform, "RELAY CODE:", "", new Vector2(0, -30));
            _joinIpInput = CreateLabeledInput("JoinIPInput", _joinPanelGo.transform, "DIRECT IP:", "127.0.0.1", new Vector2(0, -110));
            _joinPortInput = CreateLabeledInput("JoinPortInput", _joinPanelGo.transform, "PORT:", "7777", new Vector2(0, -190));

            CreateCyberButton("ConnectClientBtn", _joinPanelGo.transform, "CONNECT MATCH", new Vector2(0, -260), neonCyan, OnClickSmartConnectClient);
            CreateCyberButton("JoinBackBtn", _joinPanelGo.transform, "BACK", new Vector2(0, -325), neonMagenta, ShowMainSubpanel);
        }

        private GameObject CreatePanel(string name, Transform parent, Vector2 size)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            var pImg = panel.AddComponent<Image>();
            pImg.color = panelColor;
            var pRt = panel.GetComponent<RectTransform>();
            pRt.sizeDelta = size;
            var outline = panel.AddComponent<Outline>();
            outline.effectColor = neonCyan;
            outline.effectDistance = new Vector2(3, 3);
            return panel;
        }

        private void CreateHeader(string titleText, string subtitleText, Transform parent, Vector2 pos)
        {
            GameObject title = new GameObject("Title", typeof(RectTransform));
            title.transform.SetParent(parent, false);
            var tTxt = title.AddComponent<TextMeshProUGUI>();
            tTxt.text = titleText;
            tTxt.fontSize = 64;
            tTxt.alignment = TextAlignmentOptions.Center;
            tTxt.fontStyle = FontStyles.Bold;
            tTxt.color = Color.white;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchoredPosition = pos;
            tRt.sizeDelta = new Vector2(750, 80);

            GameObject sub = new GameObject("Subtitle", typeof(RectTransform));
            sub.transform.SetParent(parent, false);
            var sTxt = sub.AddComponent<TextMeshProUGUI>();
            sTxt.text = subtitleText;
            sTxt.fontSize = 20;
            sTxt.alignment = TextAlignmentOptions.Center;
            sTxt.color = neonCyan;
            var sRt = sub.GetComponent<RectTransform>();
            sRt.anchoredPosition = new Vector2(pos.x, pos.y - 55);
            sRt.sizeDelta = new Vector2(750, 40);
        }

        private TMP_InputField CreateLabeledInput(string name, Transform parent, string labelText, string defaultValue, Vector2 pos)
        {
            GameObject container = new GameObject(name, typeof(RectTransform));
            container.transform.SetParent(parent, false);
            var cRt = container.GetComponent<RectTransform>();
            cRt.anchoredPosition = pos;
            cRt.sizeDelta = new Vector2(650, 50);

            GameObject lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(container.transform, false);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text = labelText;
            lbl.fontSize = 20;
            lbl.color = Color.white;
            lbl.alignment = TextAlignmentOptions.Left;
            var lRt = lblGo.GetComponent<RectTransform>();
            lRt.anchoredPosition = new Vector2(-180, 0);
            lRt.sizeDelta = new Vector2(250, 40);

            GameObject inputGo = new GameObject("InputField", typeof(RectTransform));
            inputGo.transform.SetParent(container.transform, false);
            var img = inputGo.AddComponent<Image>();
            img.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
            var iRt = inputGo.GetComponent<RectTransform>();
            iRt.anchoredPosition = new Vector2(100, 0);
            iRt.sizeDelta = new Vector2(300, 45);
            var outline = inputGo.AddComponent<Outline>();
            outline.effectColor = neonCyan;

            GameObject textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(inputGo.transform, false);
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.fontSize = 22;
            txt.color = Color.white;
            var tRt = textGo.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;

            var inputField = inputGo.AddComponent<TMP_InputField>();
            inputField.textComponent = txt;
            inputField.text = defaultValue;

            return inputField;
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
            bRt.sizeDelta = new Vector2(280, 55);
            var outline = btn.AddComponent<Outline>();
            outline.effectColor = accent;
            outline.effectDistance = new Vector2(2, 2);

            GameObject txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(btn.transform, false);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 20;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;
            txt.color = Color.white;
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;

            return bBtn;
        }

        private void CreateDecoration(string sym, Transform parent, Vector2 pos, float size, Color color)
        {
            GameObject dec = new GameObject("Deco", typeof(RectTransform));
            dec.transform.SetParent(parent, false);
            var txt = dec.AddComponent<TextMeshProUGUI>();
            txt.text = sym;
            txt.fontSize = size;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = color;
            var rt = dec.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(250, 50);
        }
    }
}