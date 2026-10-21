using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

namespace NGO.Networking
{
    public class NetworkMenu : MonoBehaviour
    {
        public static NetworkMenu Instance { get; private set; }

        [Header("Scene Navigation")]
        [SerializeField] private string mainMenuScene = "MainMenuScene";

        [Header("Submenus")]
        [SerializeField] private HostMenu hostMenuScript;
        [SerializeField] private JoinMenu joinMenuScript;

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
        }

        private void Start()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            Application.runInBackground = true;

            if (hostMenuScript == null) hostMenuScript = GetComponent<HostMenu>() ?? gameObject.AddComponent<HostMenu>();
            if (joinMenuScript == null) joinMenuScript = GetComponent<JoinMenu>() ?? gameObject.AddComponent<JoinMenu>();

            BuildCyberpunkUI();
        }

        public void ShowMainMenuPanel()
        {
            if (_canvasRoot != null) _canvasRoot.SetActive(true);
            if (hostMenuScript != null) hostMenuScript.HideMenu();
            if (joinMenuScript != null) joinMenuScript.HideMenu();
        }

        public void ShowHostSubmenu()
        {
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
            if (hostMenuScript != null) hostMenuScript.ShowMenu();
        }

        public void ShowJoinSubmenu()
        {
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
            if (joinMenuScript != null) joinMenuScript.ShowMenu();
        }

        public void GoBackToMainMenuScene()
        {
            SceneManager.LoadScene(mainMenuScene);
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
            pRt.sizeDelta = new Vector2(800, 650);
            var outline = panel.AddComponent<Outline>();
            outline.effectColor = neonCyan;
            outline.effectDistance = new Vector2(3, 3);

            // Decorations
            CreateDecoration("[// NET_TERMINAL]", panel.transform, new Vector2(-260, 270), 22, neonCyan);
            CreateDecoration("[ONLINE_SERVICE]", panel.transform, new Vector2(260, 270), 22, neonMagenta);
            CreateDecoration("///", panel.transform, new Vector2(-340, 210), 30, neonYellow);
            CreateDecoration("///", panel.transform, new Vector2(340, 210), 30, neonYellow);

            // Title
            GameObject title = new GameObject("Title");
            title.transform.SetParent(panel.transform, false);
            var tTxt = title.AddComponent<TextMeshProUGUI>();
            tTxt.text = "MULTIPLAYER";
            tTxt.fontSize = 72;
            tTxt.alignment = TextAlignmentOptions.Center;
            tTxt.fontStyle = FontStyles.Bold;
            tTxt.color = Color.white;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchoredPosition = new Vector2(0, 200);
            tRt.sizeDelta = new Vector2(700, 90);

            // Subtitle
            GameObject sub = new GameObject("Subtitle");
            sub.transform.SetParent(panel.transform, false);
            var sTxt = sub.AddComponent<TextMeshProUGUI>();
            sTxt.text = "XENOBOT :: NETCODE PROTOCOL TERMINAL";
            sTxt.fontSize = 22;
            sTxt.alignment = TextAlignmentOptions.Center;
            sTxt.color = neonCyan;
            var sRt = sub.GetComponent<RectTransform>();
            sRt.anchoredPosition = new Vector2(0, 140);
            sRt.sizeDelta = new Vector2(700, 40);

            // Action Buttons
            CreateCyberButton("HostMatchButton", panel.transform, "CREATE HOST MATCH", new Vector2(0, 40), neonCyan, ShowHostSubmenu);
            CreateCyberButton("JoinMatchButton", panel.transform, "JOIN ROOM MATCH", new Vector2(0, -50), neonYellow, ShowJoinSubmenu);
            CreateCyberButton("BackButton", panel.transform, "BACK TO MAIN MENU", new Vector2(0, -140), neonMagenta, GoBackToMainMenuScene);
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
            bRt.sizeDelta = new Vector2(480, 65);
            var outline = btn.AddComponent<Outline>();
            outline.effectColor = accent;
            outline.effectDistance = new Vector2(2, 2);

            GameObject txtGo = new GameObject("Text");
            txtGo.transform.SetParent(btn.transform, false);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 26;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;
            txt.color = Color.white;
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;
        }

        private void CreateDecoration(string sym, Transform parent, Vector2 pos, float size, Color color)
        {
            GameObject dec = new GameObject("Deco");
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