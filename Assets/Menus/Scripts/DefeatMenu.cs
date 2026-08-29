using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using NGO.Networking;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Menus.Scripts
{
    public class DefeatMenu : MonoBehaviour
    {
        [Header("Settings")]
        public string levelsMenuScene = "LevelsMenuScene";
        public Color loseColor = new Color(0.6f, 0.1f, 0.1f, 0.9f);
        public Color accentColor = Color.gray;

        private GameObject _canvasRoot;
        private bool _isDisplayed = false;
        private bool _hasDetectedPlayers = false;

        private void Start()
        {
            // Initially hidden, waits for L key or all players destroyed
        }

        private void Update()
        {
            // Manual trigger
            if (!_isDisplayed && Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            {
                TriggerDefeat();
                return;
            }

            // Automatic trigger: check for players
            if (!_isDisplayed)
            {
                GameObject[] players = GameObject.FindGameObjectsWithTag("Player");

                if (players.Length > 0)
                {
                    _hasDetectedPlayers = true;
                }
                else if (_hasDetectedPlayers)
                {
                    // All players were present but now they are gone
                    TriggerDefeat();
                }
            }
        }

        /// <summary>
        /// Public method to trigger the defeat menu.
        /// </summary>
        public void TriggerDefeat()
        {
            if (_isDisplayed) return;

            _isDisplayed = true;
            BuildUI();
            UpdateLevelsData();
        }

        private void BuildUI()
        {
            _canvasRoot = new GameObject("DefeatMenu_Canvas");
            Canvas canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _canvasRoot.AddComponent<GraphicRaycaster>();

            // Background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(_canvasRoot.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0, 0, 0, 0.9f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;

            // Main Panel
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_canvasRoot.transform, false);
            var pImg = panel.AddComponent<Image>();
            pImg.color = loseColor;
            var pRt = panel.GetComponent<RectTransform>();
            pRt.sizeDelta = new Vector2(800, 700);
            panel.AddComponent<Outline>().effectColor = Color.black;

            // Decorations (Failure symbols)
            CreateDecoration("X", panel.transform, new Vector2(-300, 180), 60);
            CreateDecoration("X", panel.transform, new Vector2(300, 180), 60);
            CreateDecoration("!", panel.transform, new Vector2(-250, 220), 50);
            CreateDecoration("!", panel.transform, new Vector2(250, 220), 50);
            CreateDecoration("?", panel.transform, new Vector2(0, 260), 70);

            // Title
            GameObject title = new GameObject("Title");
            title.transform.SetParent(panel.transform, false);
            var tTxt = title.AddComponent<TextMeshProUGUI>();
            tTxt.text = "DEFEAT";
            tTxt.fontSize = 80;
            tTxt.alignment = TextAlignmentOptions.Center;
            tTxt.fontStyle = FontStyles.Bold;
            tTxt.color = Color.white;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchoredPosition = new Vector2(0, 250);
            tRt.sizeDelta = new Vector2(700, 100);

            // Subtitle
            GameObject sub = new GameObject("Subtitle");
            sub.transform.SetParent(panel.transform, false);
            var sTxt = sub.AddComponent<TextMeshProUGUI>();
            sTxt.text = "LEVEL INCOMPLETE UNFORTUNATELY";
            sTxt.fontSize = 30;
            sTxt.alignment = TextAlignmentOptions.Center;
            sTxt.color = Color.yellow;
            var sRt = sub.GetComponent<RectTransform>();
            sRt.anchoredPosition = new Vector2(0, 180);
            sRt.sizeDelta = new Vector2(700, 50);

            // Stats
            float timeTaken = LevelsMenu.ultimoTiempoSession;
            string timeStr = string.Format("{0:00}:{1:00}", Mathf.FloorToInt(timeTaken / 60), Mathf.FloorToInt(timeTaken % 60));

            GameObject stats = new GameObject("Stats");
            stats.transform.SetParent(panel.transform, false);
            var stTxt = stats.AddComponent<TextMeshProUGUI>();
            stTxt.text = $"TIME REACHED: {timeStr}";
            stTxt.fontSize = 40;
            stTxt.alignment = TextAlignmentOptions.Center;
            stTxt.color = Color.white;
            var stRt = stats.GetComponent<RectTransform>();
            stRt.anchoredPosition = new Vector2(0, -120);
            stRt.sizeDelta = new Vector2(700, 100);

            // Button
            GameObject btn = new GameObject("ReturnButton");
            btn.transform.SetParent(panel.transform, false);
            btn.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f);
            var bBtn = btn.AddComponent<Button>();
            bBtn.onClick.AddListener(ReturnToLevels);
            var bRt = btn.GetComponent<RectTransform>();
            bRt.anchoredPosition = new Vector2(0, -250);
            bRt.sizeDelta = new Vector2(400, 80);
            btn.AddComponent<Outline>().effectColor = Color.white;

            GameObject bTxtGo = new GameObject("Text");
            bTxtGo.transform.SetParent(btn.transform, false);
            var btTxt = bTxtGo.AddComponent<TextMeshProUGUI>();
            btTxt.text = "BACK";
            btTxt.fontSize = 30;
            btTxt.alignment = TextAlignmentOptions.Center;
            btTxt.color = Color.white;
            var btRt = bTxtGo.GetComponent<RectTransform>();
            btRt.anchorMin = Vector2.zero;
            btRt.anchorMax = Vector2.one;
            btRt.sizeDelta = Vector2.zero;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void ReturnToLevels()
        {
            // Mantenemos la red activa para que LevelsMenu pueda sincronizar datos
            SceneManager.LoadScene(levelsMenuScene);
        }

        private void CreateDecoration(string sym, Transform parent, Vector2 pos, float size)
        {
            GameObject dec = new GameObject("Deco");
            dec.transform.SetParent(parent, false);
            var txt = dec.AddComponent<TextMeshProUGUI>();
            txt.text = sym;
            txt.fontSize = size;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = accentColor;
            var rt = dec.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(100, 100);
        }

        private void UpdateLevelsData()
        {
            string currentScene = LevelsMenu.ultimoNivelSession;
            float timeTaken = LevelsMenu.ultimoTiempoSession;

            if (string.IsNullOrEmpty(currentScene)) currentScene = SceneManager.GetActiveScene().name;

            Debug.Log($"[Defeat] Intentando registrar tiempo: {timeTaken} en escena: {currentScene}");

            // Buscar el nivel en la lista estática
            var level = LevelsMenu.listaNiveles.Find(n =>
                (n.escenaNombre != null && n.escenaNombre.ToLower() == currentScene.ToLower()) ||
                (n.nombreNivel != null && n.nombreNivel.Replace(" ", "").ToLower() == currentScene.Replace(" ", "").ToLower()));

            if (level != null)
            {
                level.ActualizarRecord(LevelsMenu.FormatTime(timeTaken), timeTaken, false);
                Debug.Log($"[Defeat] Tiempo registrado para {level.nombreNivel}: {level.mejorTiempo}");
            }
            else
            {
                // Si el nivel no existe (ej: empezamos desde esta escena), lo creamos
                LevelData newLevel = ScriptableObject.CreateInstance<LevelData>();
                newLevel.nombreNivel = currentScene;
                newLevel.escenaNombre = currentScene;
                newLevel.ActualizarRecord(LevelsMenu.FormatTime(timeTaken), timeTaken, false);

                LevelsMenu.listaNiveles.Add(newLevel);
                Debug.Log($"[Defeat] Nivel '{currentScene}' no existía. Creado y registrado.");
            }
        }
    }
}
