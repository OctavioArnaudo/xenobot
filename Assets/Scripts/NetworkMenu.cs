using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Runtime.Scripts.Singleton;
using Runtime.Scripts.Settings;

namespace Runtime.Scripts.Network
{
    public class NetworkMenu : CSingletonMB<NetworkMenu>
    {

        [Header("Scene Navigation")]
        [SerializeField] private string mainScene = "MainScene";
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
        private TMP_InputField _hostMaxPlayersInput;
        private TMP_InputField _hostCodeInput;
        private TMP_InputField _hostIpInput;
        private TMP_Text _hostCodeDisplay;

        // Join Inputs
        private TMP_InputField _joinNameInput;
        private TMP_InputField _joinMaxPlayersInput;
        private TMP_InputField _joinCodeInput;
        private TMP_InputField _joinIpInput;
        private TMP_Text _joinCodeDisplay;

        // Active Codes Carousel for Join Status Bar (PlayerPrefs-backed for cross-instance persistence)
        public static System.Collections.Generic.List<string> GetActiveCodes()
        {
            string raw = PlayerPrefs.GetString("ActiveCodes", "");
            var list = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (string c in raw.Split(','))
                {
                    if (!string.IsNullOrEmpty(c)) list.Add(c.Trim());
                }
            }
            return list;
        }

        public static void AddActiveCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            var list = GetActiveCodes();
            if (!list.Contains(code))
            {
                list.Add(code);
                PlayerPrefs.SetString("ActiveCodes", string.Join(",", list));
                PlayerPrefs.Save();
            }
        }

        public static void RemoveActiveCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            var list = GetActiveCodes();
            if (list.Contains(code))
            {
                list.Remove(code);
                PlayerPrefs.SetString("ActiveCodes", string.Join(",", list));
                PlayerPrefs.Save();
            }
        }

        private static string _currentSessionId = "";
        private float _ugsQueryTimer = 0f;

        public static async System.Threading.Tasks.Task RefreshActiveCodesFromUGSAsync()
        {
            try
            {
                if (Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Initialized &&
                    Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
                {
                    var queryResponse = await Unity.Services.Multiplayer.MultiplayerService.Instance.QuerySessionsAsync(new Unity.Services.Multiplayer.QuerySessionsOptions());
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var s in queryResponse.Results)
                    {
                        if (s.Properties != null && s.Properties.ContainsKey("JoinCode"))
                        {
                            string c = s.Properties["JoinCode"].Value;
                            if (!string.IsNullOrEmpty(c)) list.Add(c.ToUpper());
                        }
                    }
                    if (list.Count > 0)
                    {
                        PlayerPrefs.SetString("ActiveCodes", string.Join(",", list));
                        PlayerPrefs.Save();
                    }
                }
            }
            catch { }
        }

        public static async void PublishCodeToUGS(string code, int maxPlayers)
        {
            try
            {
                if (Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Initialized &&
                    Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
                {
                    var options = new Unity.Services.Multiplayer.CreateSessionOptions
                    {
                        MaxPlayers = maxPlayers,
                        IsPrivate = false,
                        Properties = new System.Collections.Generic.Dictionary<string, Unity.Services.Multiplayer.SessionProperty>
                        {
                            { "JoinCode", new Unity.Services.Multiplayer.SessionProperty(code) }
                        }
                    };
                    var session = await Unity.Services.Multiplayer.MultiplayerService.Instance.CreateSessionAsync("XenobotRoom", options);
                    _currentSessionId = session.Id;
                }
            }
            catch { }
        }

        public static async void RemoveCodeFromUGS()
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentSessionId) &&
                    Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Initialized &&
                    Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
                {
                    await Unity.Services.Multiplayer.MultiplayerService.Instance.DeleteSessionAsync(_currentSessionId);
                    _currentSessionId = "";
                }
            }
            catch { }
        }

        private int _carouselIndex = 0;
        private float _carouselTimer = 0f;

        protected override void Awake()
        {
            base.Awake();
        }

        private void OnApplicationQuit()
        {
            RemoveCodeFromUGS();
            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost))
            {
                string code = PlayerPrefs.GetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.LastJoinCode), "");
                if (!string.IsNullOrEmpty(code))
                {
                    RemoveActiveCode(code);
                }
            }
        }

        private void OnEnable()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            }
        }

        private void OnDisable()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            Debug.Log($"[NetworkMenu] Client connected successfully! ClientId: {clientId}");
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            {
                if (NetworkManager.Singleton.LocalClientId == clientId)
                    LoadLobbyScene();
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            Debug.LogWarning($"[NetworkMenu] Client disconnected! ClientId: {clientId}");
        }

        private void Start()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Application.runInBackground = true;

            BuildCyberpunkUI();
            ShowMainSubpanel();
        }

        private void Update()
        {
            _ugsQueryTimer += Time.unscaledDeltaTime;
            if (_ugsQueryTimer >= 5.0f)
            {
                _ugsQueryTimer = 0f;
                _ = RefreshActiveCodesFromUGSAsync();
            }

            if (_joinCodeDisplay != null)
            {
                var codes = GetActiveCodes();
                if (codes == null || codes.Count == 0)
                {
                    _joinCodeDisplay.text = "NO ACTIVE ROOMS";
                }
                else
                {
                    _carouselTimer += Time.unscaledDeltaTime;
                    if (_carouselTimer >= 2.0f)
                    {
                        _carouselTimer = 0f;
                        _carouselIndex = (_carouselIndex + 1) % codes.Count;
                    }
                    string displayStr = "";
                    for (int i = 0; i < codes.Count; i++)
                    {
                        int idx = (_carouselIndex + i) % codes.Count;
                        displayStr += codes[idx] + " ";
                    }
                    _joinCodeDisplay.text = displayStr.Trim();
                }
            }

            if (_hostCodeDisplay != null)
            {
                int playersCount = 1;
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    playersCount = Mathf.Max(1, NetworkManager.Singleton.ConnectedClients.Count);
                }
                string ugsStatus = GetUgsStatus();
                _hostCodeDisplay.text = $"PLAYERS: {playersCount} SERVER: {ugsStatus}";
            }
        }

        private string GetUgsStatus()
        {
            try
            {
                if (Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Initialized)
                {
                    return "1";
                }
            }
            catch { }
            return "0";
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

        public void GoBackToMainScene()
        {
            SceneManager.LoadScene(mainScene);
        }

        private void SaveHostSettings()
        {
            if (_hostNameInput != null) PlayerPrefs.SetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.UserName), _hostNameInput.text);
            if (_hostMaxPlayersInput != null && int.TryParse(_hostMaxPlayersInput.text, out int max))
                PlayerPrefs.SetInt(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.MaxPlayers), max);
            PlayerPrefs.Save();
        }

        private void SaveJoinSettings()
        {
            if (_joinNameInput != null) PlayerPrefs.SetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.UserName), _joinNameInput.text);
            PlayerPrefs.Save();
        }

        public async void OnClickStartHostMatch()
        {
            SaveHostSettings();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
                float timeout = 2f;
                while (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && timeout > 0f)
                {
                    timeout -= Time.unscaledDeltaTime;
                    await System.Threading.Tasks.Task.Yield();
                }
            }

            ushort port = 7777;
            string ipInput = _hostIpInput != null ? _hostIpInput.text.Trim() : "127.0.0.1:7777";
            string ip = "127.0.0.1";

            if (!string.IsNullOrEmpty(ipInput))
            {
                if (ipInput.Contains(":"))
                {
                    string[] parts = ipInput.Split(':');
                    if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0])) ip = parts[0].Trim();
                    if (parts.Length > 1 && ushort.TryParse(parts[1].Trim(), out ushort parsedPort)) port = parsedPort;
                }
                else
                {
                    ip = ipInput;
                }
            }

            // Prioritize user's manual code if provided in _hostCodeInput
            string manualCode = _hostCodeInput != null ? _hostCodeInput.text.Trim().ToUpper() : "";
            string code = manualCode;

            bool relaySuccess = false;
            var transport = NetworkManager.Singleton != null ? (NetworkManager.Singleton.GetComponent<UnityTransport>() ?? NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport) : null;

            if (string.IsNullOrEmpty(manualCode) && transport != null)
            {
                try
                {
                    if (Unity.Services.Core.UnityServices.State != Unity.Services.Core.ServicesInitializationState.Initialized)
                    {
                        await Unity.Services.Core.UnityServices.InitializeAsync();
                    }
                    if (!Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
                    {
                        await Unity.Services.Authentication.AuthenticationService.Instance.SignInAnonymouslyAsync();
                    }

                    int maxPlayers = PlayerPrefs.GetInt(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.MaxPlayers), 4);
                    var allocation = await Unity.Services.Relay.RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
                    string joinCode = await Unity.Services.Relay.RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                    if (!string.IsNullOrEmpty(joinCode))
                    {
                        code = joinCode.ToUpper();
                        transport.SetRelayServerData(
                            allocation.RelayServer.IpV4,
                            (ushort)allocation.RelayServer.Port,
                            allocation.AllocationIdBytes,
                            allocation.Key,
                            allocation.ConnectionData
                        );
                        relaySuccess = true;
                        PublishCodeToUGS(code, maxPlayers);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[NetworkMenu] Relay Create failed: {ex.Message}. Using manual/default code.");
                }
            }

            if (string.IsNullOrEmpty(code))
            {
                code = "XENO42";
            }

            PlayerPrefs.SetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.LastJoinCode), code);
            PlayerPrefs.Save();
            AddActiveCode(code);

            if (!relaySuccess && transport != null)
            {
                SetUnityTransportAddress(ip, port);
            }

            if (_hostCodeDisplay != null) _hostCodeDisplay.text = $"PLAYERS: 1 SERVER: {GetUgsStatus()}";

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.StartHost())
            {
                LoadLobbyScene();
            }
        }

        public async void OnClickSmartConnectClient()
        {
            SaveJoinSettings();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
                float timeout = 2f;
                while (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && timeout > 0f)
                {
                    timeout -= Time.unscaledDeltaTime;
                    await System.Threading.Tasks.Task.Yield();
                }
            }

            string roomCode = _joinCodeInput != null ? _joinCodeInput.text.Trim().ToUpper() : "";
            string ipPortRaw = _joinIpInput != null ? _joinIpInput.text.Trim() : "";

            var transport = NetworkManager.Singleton != null ? (NetworkManager.Singleton.GetComponent<UnityTransport>() ?? NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport) : null;
            bool connectedViaRelay = false;

            if (!string.IsNullOrEmpty(roomCode) && transport != null)
            {
                try
                {
                    if (Unity.Services.Core.UnityServices.State != Unity.Services.Core.ServicesInitializationState.Initialized)
                    {
                        await Unity.Services.Core.UnityServices.InitializeAsync();
                    }
                    if (!Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
                    {
                        await Unity.Services.Authentication.AuthenticationService.Instance.SignInAnonymouslyAsync();
                    }

                    var allocation = await Unity.Services.Relay.RelayService.Instance.JoinAllocationAsync(roomCode);
                    transport.SetRelayServerData(
                        allocation.RelayServer.IpV4,
                        (ushort)allocation.RelayServer.Port,
                        allocation.AllocationIdBytes,
                        allocation.Key,
                        allocation.ConnectionData,
                        allocation.HostConnectionData
                    );

                    PlayerPrefs.SetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.LastJoinCode), roomCode);
                    PlayerPrefs.Save();
                    connectedViaRelay = true;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[NetworkMenu] Relay Join failed for code {roomCode}: {ex.Message}. Falling back to Direct IP.");
                    RemoveActiveCode(roomCode);
                }
            }

            if (!connectedViaRelay)
            {
                string ip = "127.0.0.1";
                ushort port = 7777;

                if (!string.IsNullOrEmpty(ipPortRaw))
                {
                    if (ipPortRaw.Contains(":"))
                    {
                        string[] parts = ipPortRaw.Split(':');
                        if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0])) ip = parts[0].Trim();
                        if (parts.Length > 1 && ushort.TryParse(parts[1].Trim(), out ushort parsedPort)) port = parsedPort;
                    }
                    else
                    {
                        ip = ipPortRaw;
                    }
                }

                SetUnityTransportAddress(ip, port);
            }

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.StartClient();
            }
        }

        private void SetUnityTransportAddress(string ip, ushort port)
        {
            if (NetworkManager.Singleton != null)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>()
                             ?? NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
                if (transport != null)
                {
                    transport.SetConnectionData(ip, port);
                }
            }
        }

        private void LoadLobbyScene()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(lobbySceneName);
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

        private void ConfigureCodeInput(TMP_InputField inputField)
        {
            if (inputField == null) return;
            inputField.characterLimit = 6;
            inputField.onValueChanged.AddListener((val) =>
            {
                string filtered = "";
                foreach (char c in val)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        filtered += char.ToUpper(c);
                        if (filtered.Length >= 6) break;
                    }
                }
                if (inputField.text != filtered)
                {
                    inputField.text = filtered;
                }
            });
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

            // Dark Background
            GameObject bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(_canvasRoot.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.01f, 0.02f, 0.05f, 0.90f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;

            // 1. MAIN PANEL
            _mainPanelGo = CreatePanel("MainPanel", _canvasRoot.transform, new Vector2(800, 600));
            CreateDecoration("[// NET_TERMINAL]", _mainPanelGo.transform, new Vector2(-260, 240), 20, neonCyan);
            CreateDecoration("[ONLINE_SERVICE]", _mainPanelGo.transform, new Vector2(260, 240), 20, neonMagenta);
            CreateHeader("MULTIPLAYER", "NETCODE PROTOCOL TERMINAL", _mainPanelGo.transform, new Vector2(0, 180));

            CreateCyberButton("HostMatchButton", _mainPanelGo.transform, "HOST", new Vector2(0, 30), neonCyan, ShowHostSubpanel);
            CreateCyberButton("JoinMatchButton", _mainPanelGo.transform, "JOIN", new Vector2(0, -50), neonYellow, ShowJoinSubpanel);
            CreateCyberButton("BackButton", _mainPanelGo.transform, "BACK", new Vector2(0, -130), neonMagenta, GoBackToMainScene);

            // 2. HOST SUBPANEL
            _hostPanelGo = CreatePanel("HostPanel", _canvasRoot.transform, new Vector2(850, 780));
            CreateHeader("HOST PROTOCOL", "// CREATE MATCH", _hostPanelGo.transform, new Vector2(0, 280));

            _hostNameInput = CreateLabeledInput("HostNameInput", _hostPanelGo.transform, "PLAYER NAME:", PlayerPrefs.GetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.UserName), "Player"), new Vector2(0, 160));
            if (_hostNameInput != null)
            {
                _hostNameInput.onValueChanged.AddListener((val) =>
                {
                    PlayerPrefs.SetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.UserName), val);
                    PlayerPrefs.Save();
                });
            }

            _hostMaxPlayersInput = CreateLabeledInput("HostMaxPlayersInput", _hostPanelGo.transform, "MAX PLAYERS:", PlayerPrefs.GetInt(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.MaxPlayers), 1).ToString(), new Vector2(0, 90));
            if (_hostMaxPlayersInput != null)
            {
                _hostMaxPlayersInput.onValueChanged.AddListener((val) =>
                {
                    if (int.TryParse(val, out int max))
                    {
                        PlayerPrefs.SetInt(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.MaxPlayers), max);
                        PlayerPrefs.Save();
                    }
                });
            }

            _hostCodeInput = CreateLabeledInput("HostCodeInput", _hostPanelGo.transform, "CODE:", "", new Vector2(0, 20));
            if (_hostCodeInput != null)
            {
                _hostCodeInput.interactable = false;
            }

            _hostIpInput = CreateLabeledInput("HostIpInput", _hostPanelGo.transform, "IP:PORT", $"{GetLocalIPAddress()}:7777", new Vector2(0, -50));

            GameObject codeBox = new GameObject("CodeBox", typeof(RectTransform));
            codeBox.transform.SetParent(_hostPanelGo.transform, false);
            var cbImg = codeBox.AddComponent<Image>();
            cbImg.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
            var cbRt = codeBox.GetComponent<RectTransform>();
            cbRt.anchoredPosition = new Vector2(0, -120);
            cbRt.sizeDelta = new Vector2(550, 45);
            codeBox.AddComponent<Outline>().effectColor = neonYellow;

            GameObject cbTxtGo = new GameObject("CodeText", typeof(RectTransform));
            cbTxtGo.transform.SetParent(codeBox.transform, false);
            _hostCodeDisplay = cbTxtGo.AddComponent<TextMeshProUGUI>();
            _hostCodeDisplay.text = $"PLAYERS: 1 SERVER: {GetUgsStatus()}";
            _hostCodeDisplay.fontSize = 20;
            _hostCodeDisplay.alignment = TextAlignmentOptions.Center;
            _hostCodeDisplay.color = neonYellow;
            var cbTxtRt = cbTxtGo.GetComponent<RectTransform>();
            cbTxtRt.anchorMin = Vector2.zero;
            cbTxtRt.anchorMax = Vector2.one;

            CreateCyberButton("StartHostMatchBtn", _hostPanelGo.transform, "MATCH", new Vector2(0, -190), neonCyan, OnClickStartHostMatch);
            CreateCyberButton("HostBackBtn", _hostPanelGo.transform, "BACK", new Vector2(0, -260), neonMagenta, ShowMainSubpanel);

            // 3. JOIN SUBPANEL
            _joinPanelGo = CreatePanel("JoinPanel", _canvasRoot.transform, new Vector2(850, 780));
            CreateHeader("JOIN PROTOCOL", "// CONNECT MATCH", _joinPanelGo.transform, new Vector2(0, 280));

            _joinNameInput = CreateLabeledInput("JoinNameInput", _joinPanelGo.transform, "PLAYER NAME:", PlayerPrefs.GetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.UserName), "Player"), new Vector2(0, 160));
            if (_joinNameInput != null)
            {
                _joinNameInput.onValueChanged.AddListener((val) =>
                {
                    PlayerPrefs.SetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.UserName), val);
                    PlayerPrefs.Save();
                });
            }

            _joinMaxPlayersInput = CreateLabeledInput("JoinMaxPlayersInput", _joinPanelGo.transform, "MAX PLAYERS:", "1", new Vector2(0, 90));
            if (_joinMaxPlayersInput != null)
            {
                _joinMaxPlayersInput.interactable = false;
            }

            string defaultJoinCode = "";
            var activeCodes = GetActiveCodes();
            if (activeCodes != null && activeCodes.Count > 0)
            {
                defaultJoinCode = activeCodes[activeCodes.Count - 1];
            }
            _joinCodeInput = CreateLabeledInput("JoinCodeInput", _joinPanelGo.transform, "CODE:", defaultJoinCode, new Vector2(0, 20));
            ConfigureCodeInput(_joinCodeInput);

            _joinIpInput = CreateLabeledInput("JoinIPInput", _joinPanelGo.transform, "IP:PORT", "127.0.0.1:7777", new Vector2(0, -50));

            GameObject joinCodeBox = new GameObject("CodeBox", typeof(RectTransform));
            joinCodeBox.transform.SetParent(_joinPanelGo.transform, false);
            var jCbImg = joinCodeBox.AddComponent<Image>();
            jCbImg.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
            var jCbRt = joinCodeBox.GetComponent<RectTransform>();
            jCbRt.anchoredPosition = new Vector2(0, -120);
            jCbRt.sizeDelta = new Vector2(550, 45);
            joinCodeBox.AddComponent<Outline>().effectColor = neonYellow;

            GameObject jCbTxtGo = new GameObject("CodeText", typeof(RectTransform));
            jCbTxtGo.transform.SetParent(joinCodeBox.transform, false);
            _joinCodeDisplay = jCbTxtGo.AddComponent<TextMeshProUGUI>();
            _joinCodeDisplay.fontSize = 20;
            _joinCodeDisplay.alignment = TextAlignmentOptions.Center;
            _joinCodeDisplay.color = neonYellow;
            var jCbTxtRt = jCbTxtGo.GetComponent<RectTransform>();
            jCbTxtRt.anchorMin = Vector2.zero;
            jCbTxtRt.anchorMax = Vector2.one;

            CreateCyberButton("ConnectClientBtn", _joinPanelGo.transform, "MATCH", new Vector2(0, -190), neonCyan, OnClickSmartConnectClient);
            CreateCyberButton("JoinBackBtn", _joinPanelGo.transform, "BACK", new Vector2(0, -260), neonMagenta, ShowMainSubpanel);
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
            tTxt.fontSize = 60;
            tTxt.alignment = TextAlignmentOptions.Center;
            tTxt.fontStyle = FontStyles.Bold;
            tTxt.color = Color.white;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchoredPosition = pos;
            tRt.sizeDelta = new Vector2(750, 75);

            GameObject sub = new GameObject("Subtitle", typeof(RectTransform));
            sub.transform.SetParent(parent, false);
            var sTxt = sub.AddComponent<TextMeshProUGUI>();
            sTxt.text = subtitleText;
            sTxt.fontSize = 20;
            sTxt.alignment = TextAlignmentOptions.Center;
            sTxt.color = neonCyan;
            var sRt = sub.GetComponent<RectTransform>();
            sRt.anchoredPosition = new Vector2(pos.x, pos.y - 50);
            sRt.sizeDelta = new Vector2(750, 35);
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
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            var lRt = lblGo.GetComponent<RectTransform>();
            lRt.anchoredPosition = new Vector2(-180, 0);
            lRt.sizeDelta = new Vector2(250, 45);

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
            txt.fontSize = 20;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            var tRt = textGo.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0.05f, 0f);
            tRt.anchorMax = new Vector2(0.95f, 1f);
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;

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
            bRt.sizeDelta = new Vector2(380, 55);
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