using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using Runtime.Scripts.Singleton;
using Runtime.Scripts.Settings;
using Runtime.Scripts.Levels;
using Runtime.Scripts.Gate;

namespace Runtime.Scripts.Pause
{
    public class PauseMenu : CSingletonMB<PauseMenu>
    {

        [Header("Settings & Navigation")]
        public string settingsSceneName = "SettingsScene";
        public string mainSceneName = "MainScene";
        public string levelsScene = "LevelsScene";

        [Header("UI Panel")]
        [SerializeField] private GameObject pauseMenuPanel;

        private GameObject _canvasRoot;
        private bool _isPaused = false;
        private SettingsMenu _settings;
        private bool _inSettings;

        protected override void Awake()
        {
            base.Awake();
        }

        private void Start()
        {
            if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
            Time.timeScale = 1f;
        }

        private void Update()
        {
            bool escapePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            // Ajustes superpuesto: Esc (o su botón de volver) lo cierra y regresa a la pausa; nunca reanuda el juego por debajo.
            if (_inSettings)
            {
                if (_settings != null) { if (escapePressed) _settings.ReturnToMenu(); return; }
                _inSettings = false;
                if (pauseMenuPanel != null) pauseMenuPanel.SetActive(true);
                if (_canvasRoot != null) _canvasRoot.SetActive(true);
                return;
            }
            // Con inventario/crafting/stats/mapa abiertos, Esc solo los cierra: la pausa no se abre encima.
            if (escapePressed && !_isPaused && (MenuGate.EscConsumed || MenuGate.HasClosableMenu)) escapePressed = false;

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
            MenuGate.Take("pause"); // mientras está en pausa no se abre ningún otro menú
            Time.timeScale = 0f;

            if (pauseMenuPanel != null)
            {
                pauseMenuPanel.SetActive(true);
            }
            else
            {
                BuildUI();
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public void ResumeGame()
        {
            if (!_isPaused) return;

            _isPaused = false;
            MenuGate.Close("pause");
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

        public void OpenSettings()
        {
            // En lugar de cargar escena, activamos el modo superposición (Overlay)
            // para no destruir el estado de red ni los prefabs del jugador.
            _settings = gameObject.AddComponent<SettingsMenu>();
            _settings.IsOverlayMode = true;
            _inSettings = true;

            // Ocultamos el menú de pausa actual para que no estorbe
            if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
        }

        public void QuitSessionOrMatch()
        {
            Time.timeScale = 1f;
            _isPaused = false;
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
            {
                Unity.Netcode.NetworkManager.Singleton.Shutdown();
            }
            SceneManager.LoadScene(!string.IsNullOrEmpty(levelsScene) ? levelsScene : mainSceneName);
        }

        private void BuildUI()
        {
            if (_canvasRoot != null) Destroy(_canvasRoot);

            _canvasRoot = new GameObject("EscapeMenu_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000;
            var scaler = _canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var dim = MenuGate.Img(_canvasRoot.transform, new Color(0f, 0f, 0f, 0.8f));
            MenuGate.Fit(dim, 0f, 0f, 1f, 1f);
            dim.GetComponent<Image>().raycastTarget = true;

            string player = PlayerPrefs.GetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.UserName), "Player");
            float t = LevelsMenu.lastSessionTime;
            var term = MenuGate.Term(_canvasRoot.transform, new Vector2(640f, 560f), "PAUSED", out var body);
            body.fontSize = 28;
            body.text = $"PLAYER<pos=35%>{player}\nSCENE<pos=35%>{SceneManager.GetActiveScene().name}\nTIMER<pos=35%>{Mathf.FloorToInt(t / 60):00}:{Mathf.FloorToInt(t % 60):00}";

            var sz = new Vector2(400f, 64f);
            MenuGate.Btn(term, "RESUME", sz, ResumeGame).anchoredPosition = new Vector2(0f, 10f);
            MenuGate.Btn(term, "SETTINGS", sz, OpenSettings).anchoredPosition = new Vector2(0f, -80f);
            MenuGate.Btn(term, "ABORT", sz, QuitSessionOrMatch).anchoredPosition = new Vector2(0f, -170f);
        }
    }
}