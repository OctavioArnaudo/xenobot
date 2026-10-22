using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using NGO.Networking;
using Levels.Data;

namespace Menus.Scripts
{
    public class PauseMenu : MonoBehaviour
    {
        public static PauseMenu Instance { get; private set; }

        [Header("Settings & Navigation")]
        public string settingsSceneName = "SettingsMenuScene";
        public string mainMenuSceneName = "MainMenuScene";
        public string levelsMenuScene = "LevelsMenuScene";

        [Header("UI Panel (Optional - Auto-built if null)")]
        [SerializeField] private GameObject pauseMenuPanel;

        [Header("Cyberpunk Theme Colors")]
        public Color panelColor = new Color(0.04f, 0.08f, 0.12f, 0.92f);
        public Color neonCyan = new Color(0f, 1f, 0.95f, 1f);
        public Color neonMagenta = new Color(1f, 0f, 0.45f, 1f);
        public Color neonYellow = new Color(1f, 0.85f, 0f, 1f);

        private GameObject _canvasRoot;
        private bool _isPaused = false;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
            Time.timeScale = 1f;
        }

        private void Update()
        {
            bool escapePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

            if (escapePressed)
            {
                if (_isPaused) ResumeGame();
                else PauseGame();
            }
        }

        public void PauseGame()
        {
            if (_isPaused) return;

            _isPaused = true;
            Time.timeScale = 0f;

            if (pauseMenuPanel != null)
            {
                pauseMenuPanel.SetActive(true);
            }
            else
            {
                BuildCyberpunkUI();
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public void ResumeGame()
        {
            if (!_isPaused) return;

            _isPaused = false;
            Time.timeScale = 1f;

            if (pauseMenuPanel != null)
            {
                pauseMenuPanel.SetActive(false);
            }

            if (_canvasRoot != null)
            {
                Destroy(_canvasRoot);
            }

            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        public void RestartLevel()
        {
            Time.timeScale = 1f;
            _isPaused = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public void OpenSettings()
        {
            Time.timeScale = 1f;
            _isPaused = false;
            SettingsMenu.PreviousScene = SceneManager.GetActiveScene().name;
            SceneManager.LoadScene(settingsSceneName);
        }

        public void QuitSessionOrMatch()
        {
            Time.timeScale = 1f;
            _isPaused = false;
            NetworkingService.Shutdown();
            SceneManager.LoadScene(!string.IsNullOrEmpty(levelsMenuScene) ? levelsMenuScene : mainMenuSceneName);
        }

        private void BuildCyberpunkUI()
        {
            if (_canvasRoot != null) Destroy(_canvasRoot);

            _canvasRoot = new GameObject("EscapeMenu_Canvas");
            Canvas canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000;

            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _canvasRoot.AddComponent<GraphicRaycaster>();

            // Dark Translucent Background
            GameObject bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(_canvasRoot.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.01f, 0.02f, 0.05f, 0.88f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;

            // Main Cyberpunk Panel
            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(_canvasRoot.transform, false);
            var pImg = panel.AddComponent<Image>();
            pImg.color = panelColor;
            var pRt = panel.GetComponent<RectTransform>();
            pRt.sizeDelta = new Vector2(850, 750);
            var outline = panel.AddComponent<Outline>();
            outline.effectColor = neonCyan;
            outline.effectDistance = new Vector2(3, 3);

            // Cyberpunk Corner Brackets / Symbols
            CreateDecoration("[// SYS_TACTICAL]", panel.transform, new Vector2(-280, 320), 22, neonCyan);
            CreateDecoration("[OVERRIDE_ACTIVE]", panel.transform, new Vector2(280, 320), 22, neonMagenta);
            CreateDecoration("///", panel.transform, new Vector2(-360, 260), 30, neonYellow);
            CreateDecoration("///", panel.transform, new Vector2(360, 260), 30, neonYellow);

            // Title
            GameObject title = new GameObject("Title", typeof(RectTransform));
            title.transform.SetParent(panel.transform, false);
            var tTxt = title.AddComponent<TextMeshProUGUI>();
            tTxt.text = "PAUSED";
            tTxt.fontSize = 72;
            tTxt.alignment = TextAlignmentOptions.Center;
            tTxt.fontStyle = FontStyles.Bold;
            tTxt.color = Color.white;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchoredPosition = new Vector2(0, 250);
            tRt.sizeDelta = new Vector2(750, 90);

            // Subtitle
            GameObject sub = new GameObject("Subtitle", typeof(RectTransform));
            sub.transform.SetParent(panel.transform, false);
            var sTxt = sub.AddComponent<TextMeshProUGUI>();
            sTxt.text = "TACTICAL OVERRIDE";
            sTxt.fontSize = 24;
            sTxt.alignment = TextAlignmentOptions.Center;
            sTxt.color = neonCyan;
            var sRt = sub.GetComponent<RectTransform>();
            sRt.anchoredPosition = new Vector2(0, 185);
            sRt.sizeDelta = new Vector2(750, 40);

            // Session Stats Info
            string playerName = LocalUserConfig.UserName;
            string currentScene = SceneManager.GetActiveScene().name;
            float timeTaken = LevelsMenu.ultimoTiempoSession;
            string timeStr = string.Format("{0:00}:{1:00}", Mathf.FloorToInt(timeTaken / 60), Mathf.FloorToInt(timeTaken % 60));

            GameObject stats = new GameObject("Stats", typeof(RectTransform));
            stats.transform.SetParent(panel.transform, false);
            var stTxt = stats.AddComponent<TextMeshProUGUI>();
            stTxt.text = $"PLAYER: <color=#00FFFF>{playerName}</color>   |   SCENE: <color=#FFE600>{currentScene}</color>   |   TIMER: <color=#FF007F>{timeStr}</color>";
            stTxt.fontSize = 22;
            stTxt.alignment = TextAlignmentOptions.Center;
            stTxt.color = Color.white;
            var stRt = stats.GetComponent<RectTransform>();
            stRt.anchoredPosition = new Vector2(0, 135);
            stRt.sizeDelta = new Vector2(750, 40);

            // Cyber Buttons
            CreateCyberButton("ResumeButton", panel.transform, "RESUME", new Vector2(0, 60), neonCyan, ResumeGame);
            CreateCyberButton("RestartButton", panel.transform, "RESTART", new Vector2(0, -30), neonYellow, RestartLevel);
            CreateCyberButton("SettingsButton", panel.transform, "SETTINGS", new Vector2(0, -120), neonCyan, OpenSettings);
            CreateCyberButton("QuitButton", panel.transform, "ABORT", new Vector2(0, -210), neonMagenta, QuitSessionOrMatch);
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
            bRt.sizeDelta = new Vector2(450, 65);
            var outline = btn.AddComponent<Outline>();
            outline.effectColor = accent;
            outline.effectDistance = new Vector2(2, 2);

            GameObject txtGo = new GameObject("Text", typeof(RectTransform));
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