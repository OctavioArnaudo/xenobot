using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace NGO.Networking
{
    public class JoinMenu : MonoBehaviour
    {
        public static JoinMenu Instance { get; private set; }

        [Header("UI References (Optional - Auto-built if null)")]
        [SerializeField] private TMP_InputField nameInputField;
        [SerializeField] private TMP_InputField customIdInputField;
        [SerializeField] private TMP_InputField roomCodeInputField;
        [SerializeField] private TMP_InputField ipInputField;
        [SerializeField] private TMP_InputField portInputField;

        [Header("Modular Components")]
        [SerializeField] private NetworkAddressConfigurator addressConfigurator;

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
            if (addressConfigurator == null) addressConfigurator = GetComponent<NetworkAddressConfigurator>() ?? gameObject.AddComponent<NetworkAddressConfigurator>();
        }

        public void ShowMenu()
        {
            if (nameInputField == null || roomCodeInputField == null)
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
        }

        public async void OnClickSmartConnect()
        {
            SaveSettings();
            NetworkingService.Shutdown();

            string roomCode = roomCodeInputField != null ? roomCodeInputField.text.Trim().ToUpper() : "";
            string ip = ipInputField != null ? ipInputField.text.Trim() : "";

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
                if (portInputField != null && ushort.TryParse(portInputField.text, out ushort parsedPort)) port = parsedPort;
                if (addressConfigurator != null) addressConfigurator.SetIPAddress(ip, port);
                NetworkingService.StartClient(ip, port);
            }
        }

        public void OnClickBack()
        {
            HideMenu();
            if (NetworkMenu.Instance != null) NetworkMenu.Instance.ShowMainMenuPanel();
        }

        private void BuildCyberpunkUI()
        {
            if (_canvasRoot != null) Destroy(_canvasRoot);

            _canvasRoot = new GameObject("JoinMenu_Canvas");
            Canvas canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1100;

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

            // Main Panel
            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(_canvasRoot.transform, false);
            var pImg = panel.AddComponent<Image>();
            pImg.color = panelColor;
            var pRt = panel.GetComponent<RectTransform>();
            pRt.sizeDelta = new Vector2(850, 750);
            var outline = panel.AddComponent<Outline>();
            outline.effectColor = neonCyan;
            outline.effectDistance = new Vector2(3, 3);

            // Title
            GameObject title = new GameObject("Title", typeof(RectTransform));
            title.transform.SetParent(panel.transform, false);
            var tTxt = title.AddComponent<TextMeshProUGUI>();
            tTxt.text = "JOIN PROTOCOL";
            tTxt.fontSize = 64;
            tTxt.alignment = TextAlignmentOptions.Center;
            tTxt.fontStyle = FontStyles.Bold;
            tTxt.color = Color.white;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchoredPosition = new Vector2(0, 260);
            tRt.sizeDelta = new Vector2(750, 80);

            // Subtitle
            GameObject sub = new GameObject("Subtitle", typeof(RectTransform));
            sub.transform.SetParent(panel.transform, false);
            var sTxt = sub.AddComponent<TextMeshProUGUI>();
            sTxt.text = "// CONNECT TO RELAY OR DIRECT IP";
            sTxt.fontSize = 22;
            sTxt.alignment = TextAlignmentOptions.Center;
            sTxt.color = neonCyan;
            var sRt = sub.GetComponent<RectTransform>();
            sRt.anchoredPosition = new Vector2(0, 205);
            sRt.sizeDelta = new Vector2(750, 40);

            // Inputs
            nameInputField = CreateLabeledInput("NameInput", panel.transform, "OPERATOR NAME:", LocalUserConfig.UserName, new Vector2(0, 130));
            customIdInputField = CreateLabeledInput("CustomIdInput", panel.transform, "CUSTOM ID:", LocalUserConfig.UserCustomID.ToString(), new Vector2(0, 50));
            roomCodeInputField = CreateLabeledInput("RoomCodeInput", panel.transform, "RELAY CODE:", "", new Vector2(0, -30));
            ipInputField = CreateLabeledInput("IPInput", panel.transform, "DIRECT IP:", "127.0.0.1", new Vector2(0, -110));
            portInputField = CreateLabeledInput("PortInput", panel.transform, "PORT:", "7777", new Vector2(0, -190));

            // Action Buttons
            CreateCyberButton("ConnectButton", panel.transform, "CONNECT MATCH", new Vector2(0, -250), neonCyan, OnClickSmartConnect);
            CreateCyberButton("BackButton", panel.transform, "BACK TO MENU", new Vector2(0, -320), neonMagenta, OnClickBack);
        }

        private TMP_InputField CreateLabeledInput(string name, Transform parent, string labelText, string defaultValue, Vector2 pos)
        {
            GameObject container = new GameObject(name, typeof(RectTransform));
            container.transform.SetParent(parent, false);
            var cRt = container.GetComponent<RectTransform>();
            cRt.anchoredPosition = pos;
            cRt.sizeDelta = new Vector2(650, 50);

            // Label
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

            // Input Field Box
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

        private void CreateCyberButton(string name, Transform parent, string label, Vector2 pos, Color accent, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btn = new GameObject(name, typeof(RectTransform));
            btn.transform.SetParent(parent, false);
            var img = btn.AddComponent<Image>();
            img.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
            var bBtn = btn.AddComponent<Button>();
            bBtn.onClick.AddListener(onClick);
            var bRt = btn.GetComponent<RectTransform>();
            bRt.anchoredPosition = pos;
            bRt.sizeDelta = new Vector2(450, 55);
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
        }
    }
}