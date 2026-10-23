using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using Unity.Netcode;

namespace Runtime.Scripts.Main
{
    public class MainMenu : MonoBehaviour
    {
        [Header("Scene Configuration")]
        public string gameScene = "NetworkScene";
        public string levelsScene = "LevelsScene";
        public string creditsScene = "CreditsScene";
        public string settingsScene = "SettingsScene";

        [Header("UI References (Optional)")]
        [SerializeField] private GameObject coverImageObject;

        [Header("Visual Style")]
        public Color titleColor = Color.cyan;
        public Color buttonsColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        public Color buttonTextColor = Color.white;

        private void Start()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            var allNMs = Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
            foreach (var nm in allNMs)
            {
                if (nm.IsListening) nm.Shutdown();
                Destroy(nm.gameObject);
            }

            SetupUI();
        }

        private void SetupUI()
        {
            GameObject canvasObj = new GameObject("MainMenuCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasObj.AddComponent<GraphicRaycaster>();

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            CreateAdaptiveImage(canvasObj.transform, "DarkOverlay", new Vector2(0.5f, 0.5f), new Vector2(1.2f, 1.2f), new Color(0, 0, 0, 0.5f));

            if (coverImageObject != null)
            {
                coverImageObject.transform.SetParent(canvasObj.transform);
                RectTransform rt = coverImageObject.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition3D = Vector3.zero;
                rt.localScale = Vector3.one;
            }
            else
            {
                CreateAdaptiveImage(canvasObj.transform, "CoverPlaceholder", new Vector2(0.5f, 0.5f), new Vector2(0.35f, 0.45f), new Color(0.3f, 0.3f, 0.3f, 0.8f));
            }

            CreateAdaptiveText(canvasObj.transform, "XENOBOT", new Vector2(0.5f, 0.85f), titleColor, true);

            CreateAdaptiveButton(canvasObj.transform, "SETTINGS", new Vector2(0.2f, 0.6f), OpenSettings, buttonsColor);
            CreateAdaptiveButton(canvasObj.transform, "LEVELS", new Vector2(0.2f, 0.4f), OpenLevels, buttonsColor);
            CreateAdaptiveButton(canvasObj.transform, "CREDITS", new Vector2(0.8f, 0.6f), OpenCredits, buttonsColor);
            CreateAdaptiveButton(canvasObj.transform, "QUIT", new Vector2(0.8f, 0.4f), Quit, new Color(0.7f, 0.1f, 0.1f));
            CreateAdaptiveButton(canvasObj.transform, "PLAY", new Vector2(0.5f, 0.18f), Play, new Color(0.1f, 0.7f, 0.1f));
        }

        private void CreateAdaptiveText(Transform parent, string content, Vector2 anchorPos, Color color, bool isUpper)
        {
            GameObject textObj = new GameObject("TitleText");
            textObj.transform.SetParent(parent);
            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = isUpper ? content.ToUpper() : content;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 20;
            tmp.fontSizeMax = 250;

            RectTransform rect = textObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorPos.x - 0.45f, anchorPos.y - 0.1f);
            rect.anchorMax = new Vector2(anchorPos.x + 0.45f, anchorPos.y + 0.1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition3D = Vector3.zero;
            rect.localScale = Vector3.one;
        }

        private void CreateAdaptiveImage(Transform parent, string name, Vector2 anchorPos, Vector2 sizeRatio, Color color)
        {
            GameObject imgObj = new GameObject(name);
            imgObj.transform.SetParent(parent);
            Image img = imgObj.AddComponent<Image>();
            img.color = color;

            RectTransform rect = imgObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorPos.x - (sizeRatio.x / 2), anchorPos.y - (sizeRatio.y / 2));
            rect.anchorMax = new Vector2(anchorPos.x + (sizeRatio.x / 2), anchorPos.y + (sizeRatio.y / 2));
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition3D = Vector3.zero;
            rect.localScale = Vector3.one;
        }

        private void CreateAdaptiveButton(Transform parent, string label, Vector2 anchorPos, UnityEngine.Events.UnityAction action, Color bgColor)
        {
            GameObject btnObj = new GameObject("Button_" + label);
            btnObj.transform.SetParent(parent);

            Image img = btnObj.AddComponent<Image>();
            img.color = bgColor;

            Button btn = btnObj.AddComponent<Button>();
            btn.onClick.AddListener(action);

            RectTransform rect = btnObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorPos.x - 0.1f, anchorPos.y - 0.05f);
            rect.anchorMax = new Vector2(anchorPos.x + 0.1f, anchorPos.y + 0.05f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition3D = Vector3.zero;
            rect.localScale = Vector3.one;

            GameObject txtObj = new GameObject("Label");
            txtObj.transform.SetParent(btnObj.transform);
            TextMeshProUGUI tmp = txtObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.color = buttonTextColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;

            RectTransform txtRect = txtObj.GetComponent<RectTransform>();
            txtRect.anchorMin = new Vector2(0.1f, 0.1f);
            txtRect.anchorMax = new Vector2(0.9f, 0.9f);
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;
            txtRect.anchoredPosition3D = Vector3.zero;
            txtRect.localScale = Vector3.one;
        }

        public void Play()
        {
            LoadScene(gameScene);
        }

        public void OpenLevels() => LoadScene(levelsScene);
        public void OpenCredits() => LoadScene(creditsScene);
        public void OpenSettings() => LoadScene(settingsScene);

        private void LoadScene(string sceneName)
        {
            if (!string.IsNullOrEmpty(sceneName)) SceneManager.LoadScene(sceneName);
        }

        public void Quit() => Application.Quit();
    }
}