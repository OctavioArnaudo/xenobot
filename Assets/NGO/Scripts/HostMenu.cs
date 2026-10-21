using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Net;
using System.Net.Sockets;

namespace NGO.Networking
{
    public class HostMenu : MonoBehaviour
    {
        public static HostMenu Instance { get; private set; }

        [Header("Scene Configuration")]
        [SerializeField] private string targetScene = "LobbyScene";

        [Header("UI References (Optional - Auto-built if null)")]
        [SerializeField] private TMP_InputField nameInputField;
        [SerializeField] private TMP_InputField customIdInputField;
        [SerializeField] private TMP_InputField maxPlayersInput;
        [SerializeField] private TMP_Text roomCodeDisplay;
        [SerializeField] private TMP_Text ipDisplay;

        [Header("Modular Components")]
        [SerializeField] private NetworkSceneLoader sceneLoader;

        [Header("Cyberpunk Theme Colors")]
        public Color panelColor = new Color(0.04f, 0.08f, 0.12f, 0.92f);
        public Color neonCyan = new Color(0f, 1f, 0.95f, 1f);
        public Color neonMagenta = new Color(1f, 0f, 0.45f, 1f);
        public Color neonYellow = new Color(1f, 0.85f, 0f, 1f);

        private GameObject _canvasRoot;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (sceneLoader == null) sceneLoader = GetComponent<NetworkSceneLoader>() ?? gameObject.AddComponent<NetworkSceneLoader>();
        }

        public void ShowMenu()
        {
            if (nameInputField == null || roomCodeDisplay == null)
            {
                BuildCyberpunkUI();
            }
            if (_canvasRoot != null) _canvasRoot.SetActive(true);
        }

        public void HideMenu()
        {
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
        }

        private void SaveSettings()
        {
            if (nameInputField != null) LocalUserConfig.UserName = nameInputField.text;
            if (customIdInputField != null && int.TryParse(customIdInputField.text, out int id))
                LocalUserConfig.UserCustomID = id;
            if (maxPlayersInput != null && int.TryParse(maxPlayersInput.text, out int max))
                LocalUserConfig.MaxPlayers = max;
        }

        public async void OnClickStartHost()
        {
            SaveSettings();

            int maxPlayers = LocalUserConfig.MaxPlayers;
            if (roomCodeDisplay != null) roomCodeDisplay.text = "CREATING...";

            string joinCode = await RelayManager.CreateRelay(maxPlayers);

            if (!string.IsNullOrEmpty(joinCode))
            {
                LocalUserConfig.LastJoinCode = joinCode;
                if (roomCodeDisplay != null) roomCodeDisplay.text = joinCode;

                if (NetworkingService.StartHost(isRelay: true))
                {
                    if (sceneLoader != null)
                    {
                        sceneLoader.LoadScene(targetScene);
                    }
                }
            }
            else
            {
                if (roomCodeDisplay != null) roomCodeDisplay.text = "ERROR";
            }
        }

        public void OnClickBack()
        {
            HideMenu();
            if (NetworkMenu.Instance != null) NetworkMenu.Instance.ShowMainMenuPanel();
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

            _canvasRoot = new GameObject("HostMenu_Canvas");
            Canvas canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1100;

            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _canvasRoot.AddComponent<GraphicRaycaster>();

            // Dark Translucent Background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(_canvasRoot.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.01f, 0.02f, 0.05f, 0.90f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;

            // Main Panel
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_canvasRoot.transform, false);
            var pImg = panel.AddComponent<Image>();
            pImg.color = panelColor;
            var pRt = panel.GetComponent<RectTransform>();
            pRt.sizeDelta = new Vector2(850, 750);
            var outline = panel.AddComponent<Outline>();
            outline.effectColor = neonCyan;
            outline.effectDistance = new Vector2(3, 3);

            // Title
            GameObject title = new GameObject("Title");
            title.transform.SetParent(panel.transform, false);
            var tTxt = title.AddComponent<TextMeshProUGUI>();
            tTxt.text = "HOST PROTOCOL";
            tTxt.fontSize = 64;
            tTxt.alignment = TextAlignmentOptions.Center;
            tTxt.fontStyle = FontStyles.Bold;
            tTxt.color = Color.white;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchoredPosition = new Vector2(0, 260);
            tRt.sizeDelta = new Vector2(750, 80);

            // Subtitle
            GameObject sub = new GameObject("Subtitle");
            sub.transform.SetParent(panel.transform, false);
            var sTxt = sub.AddComponent<TextMeshProUGUI>();
            sTxt.text = "// CREATE RELAY MATCH ROOM";
            sTxt.fontSize = 22;
            sTxt.alignment = TextAlignmentOptions.Center;
            sTxt.color = neonCyan;
            var sRt = sub.GetComponent<RectTransform>();
            sRt.anchoredPosition = new Vector2(0, 205);
            sRt.sizeDelta = new Vector2(750, 40);

            // Inputs
            nameInputField = CreateLabeledInput("NameInput", panel.transform, "OPERATOR NAME:", LocalUserConfig.UserName, new Vector2(0, 130));
            customIdInputField = CreateLabeledInput("CustomIdInput", panel.transform, "CUSTOM ID:", LocalUserConfig.UserCustomID.ToString(), new Vector2(0, 50));
            maxPlayersInput = CreateLabeledInput("MaxPlayersInput", panel.transform, "MAX PLAYERS:", LocalUserConfig.MaxPlayers.ToString(), new Vector2(0, -30));

            // Room Code & IP Display Box
            GameObject codeBox = new GameObject("CodeBox");
            codeBox.transform.SetParent(panel.transform, false);
            var cbImg = codeBox.AddComponent<Image>();
            cbImg.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
            var cbRt = codeBox.GetComponent<RectTransform>();
            cbRt.anchoredPosition = new Vector2(0, -115);
            cbRt.sizeDelta = new Vector2(500, 50);
            var cbOutline = codeBox.AddComponent<Outline>();
            cbOutline.effectColor = neonYellow;

            GameObject cbTxtGo = new GameObject("CodeText");
            cbTxtGo.transform.SetParent(codeBox.transform, false);
            roomCodeDisplay = cbTxtGo.AddComponent<TextMeshProUGUI>();
            roomCodeDisplay.text = $"IP: {GetLocalIPAddress()}   |   RELAY: READY";
            roomCodeDisplay.fontSize = 20;
            roomCodeDisplay.alignment = TextAlignmentOptions.Center;
            roomCodeDisplay.color = neonYellow;
            var cbTxtRt = cbTxtGo.GetComponent<RectTransform>();
            cbTxtRt.anchorMin = Vector2.zero;
            cbTxtRt.anchorMax = Vector2.one;

            // Action Buttons
            CreateCyberButton("StartHostButton", panel.transform, "START HOST SESSION", new Vector2(0, -200), neonCyan, OnClickStartHost);
            CreateCyberButton("BackButton", panel.transform, "BACK TO MENU", new Vector2(0, -280), neonMagenta, OnClickBack);
        }

        private TMP_InputField CreateLabeledInput(string name, Transform parent, string labelText, string defaultValue, Vector2 pos)
        {
            GameObject container = new GameObject(name);
            container.transform.SetParent(parent, false);
            var cRt = container.GetComponent<RectTransform>();
            cRt.anchoredPosition = pos;
            cRt.sizeDelta = new Vector2(650, 50);

            // Label
            GameObject lblGo = new GameObject("Label");
            lblGo.transform.SetParent(container.transform, false);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text = labelText;
            lbl.fontSize = 20;
            lbl.color = Color.white;
            lbl.alignment = TextAlignmentOptions.Left;
            var lRt = lblGo.GetComponent<RectTransform>();
            lRt.anchoredPosition = new Vector2(-180, 0);
            lRt.sizeDelta = new Vector2(250, 40);

            // Input Field Box
            GameObject inputGo = new GameObject("InputField");
            inputGo.transform.SetParent(container.transform, false);
            var img = inputGo.AddComponent<Image>();
            img.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
            var iRt = inputGo.GetComponent<RectTransform>();
            iRt.anchoredPosition = new Vector2(100, 0);
            iRt.sizeDelta = new Vector2(300, 45);
            var outline = inputGo.AddComponent<Outline>();
            outline.effectColor = neonCyan;

            GameObject textGo = new GameObject("Text");
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

        private void CreateCyberButton(string name, Transform parent, string label, Vector2 pos, Color accent, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btn = new GameObject(name);
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
        }
    }
}