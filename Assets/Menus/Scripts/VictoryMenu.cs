using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using NGO.Networking;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Menus.Scripts
{
    public class VictoryMenu : MonoBehaviour
    {
        [Header("Settings")]
        public string levelsMenuScene = "LevelsMenuScene";
        public Color winColor = new Color(0f, 0.8f, 0.2f, 0.9f);
        public Color accentColor = Color.yellow;

        private GameObject _canvasRoot;
        private bool _isDisplayed = false;

        private void Start()
        {
            // Initially hidden, waits for V key
        }

        private void Update()
        {
            if (!_isDisplayed && Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
            {
                TriggerVictory();
            }
        }

        /// <summary>
        /// Public method to trigger the victory menu from other scripts.
        /// </summary>
        public void TriggerVictory()
        {
            if (_isDisplayed) return;

            _isDisplayed = true;
            BuildUI();
            UpdateLevelsData();
        }

        private void BuildUI()
        {
            _canvasRoot = new GameObject("VictoryMenu_Canvas");
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
            bgImg.color = new Color(0, 0, 0, 0.85f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;

            // Main Panel
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_canvasRoot.transform, false);
            var pImg = panel.AddComponent<Image>();
            pImg.color = winColor;
            var pRt = panel.GetComponent<RectTransform>();
            pRt.sizeDelta = new Vector2(800, 600);
            panel.AddComponent<Outline>().effectColor = Color.white;

            // Title
            GameObject title = new GameObject("Title");
            title.transform.SetParent(panel.transform, false);
            var tTxt = title.AddComponent<TextMeshProUGUI>();
            tTxt.text = "VICTORY!";
            tTxt.fontSize = 80;
            tTxt.alignment = TextAlignmentOptions.Center;
            tTxt.fontStyle = FontStyles.Bold;
            tTxt.color = Color.white;
            var tRt = title.GetComponent<RectTransform>();
            tRt.anchoredPosition = new Vector2(0, 200);
            tRt.sizeDelta = new Vector2(700, 100);

            // Subtitle
            GameObject sub = new GameObject("Subtitle");
            sub.transform.SetParent(panel.transform, false);
            var sTxt = sub.AddComponent<TextMeshProUGUI>();
            sTxt.text = "LEVEL COMPLETED SUCCESSFULLY";
            sTxt.fontSize = 30;
            sTxt.alignment = TextAlignmentOptions.Center;
            sTxt.color = accentColor;
            var sRt = sub.GetComponent<RectTransform>();
            sRt.anchoredPosition = new Vector2(0, 120);
            sRt.sizeDelta = new Vector2(700, 50);

            // Decorations (Celebration symbols - using standard chars to avoid font issues)
            CreateDecoration("*", panel.transform, new Vector2(-300, 180), 60);
            CreateDecoration("*", panel.transform, new Vector2(300, 180), 60);
            CreateDecoration("+", panel.transform, new Vector2(-250, 220), 50);
            CreateDecoration("+", panel.transform, new Vector2(250, 220), 50);
            CreateDecoration("!", panel.transform, new Vector2(0, 260), 70);

            // Stats
            string playerName = LocalUserConfig.UserName;
            float timeTaken = LevelsMenu.ultimoTiempoSession;
            string timeStr = string.Format("{0:00}:{1:00}", Mathf.FloorToInt(timeTaken / 60), Mathf.FloorToInt(timeTaken % 60));

            GameObject stats = new GameObject("Stats");
            stats.transform.SetParent(panel.transform, false);
            var stTxt = stats.AddComponent<TextMeshProUGUI>();
            stTxt.text = $"PLAYER: {playerName}\nTIME TAKEN: {timeStr}";
            stTxt.fontSize = 40;
            stTxt.alignment = TextAlignmentOptions.Center;
            stTxt.color = Color.white;
            var stRt = stats.GetComponent<RectTransform>();
            stRt.anchoredPosition = new Vector2(0, -20);
            stRt.sizeDelta = new Vector2(700, 150);

            // Button
            GameObject btn = new GameObject("ReturnButton");
            btn.transform.SetParent(panel.transform, false);
            btn.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f);
            var bBtn = btn.AddComponent<Button>();
            bBtn.onClick.AddListener(ReturnToLevels);
            var bRt = btn.GetComponent<RectTransform>();
            bRt.anchoredPosition = new Vector2(0, -200);
            bRt.sizeDelta = new Vector2(400, 80);
            btn.AddComponent<Outline>().effectColor = accentColor;

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
            string playerName = LocalUserConfig.UserName;

            if (string.IsNullOrEmpty(currentScene)) currentScene = SceneManager.GetActiveScene().name;

            Debug.Log($"[Victory] Registrando éxito: {playerName} | Tiempo: {timeTaken} | Escena: {currentScene}");

            var level = LevelsMenu.listaNiveles.Find(n =>
                (n.escenaNombre != null && n.escenaNombre.ToLower() == currentScene.ToLower()) ||
                (n.nombreNivel != null && n.nombreNivel.Replace(" ", "").ToLower() == currentScene.Replace(" ", "").ToLower()));

            if (level != null)
            {
                level.ActualizarRecord(LevelsMenu.FormatTime(timeTaken), timeTaken, true, playerName);
                Debug.Log($"[Victory] Nivel {level.nombreNivel} actualizado correctamente.");
            }
            else
            {
                // Crear nivel dinámicamente si no existe
                LevelData newLevel = ScriptableObject.CreateInstance<LevelData>();
                newLevel.nombreNivel = currentScene;
                newLevel.escenaNombre = currentScene;
                newLevel.ActualizarRecord(LevelsMenu.FormatTime(timeTaken), timeTaken, true, playerName);

                LevelsMenu.listaNiveles.Add(newLevel);
                Debug.Log($"[Victory] Nivel '{currentScene}' creado dinámicamente con éxito de {playerName}.");
            }
        }
    }
}
