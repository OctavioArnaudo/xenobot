using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using Unity.Netcode;

namespace Runtime.Scripts.Main
{
    public class MainMenu : NetworkBehaviour
    {
        [Header("Scene Configuration")]
        public string gameScene = "NetworkScene";
        public string levelsScene = "LevelsScene";
        public string creditsScene = "CreditsScene";
        public string settingsScene = "SettingsScene";

        [Header("Visual Style & Resources")]
        public Sprite coverSprite;
        public Color titleColor = Color.cyan;
        public Color buttonsColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        public Color playButtonColor = new Color(0.1f, 0.7f, 0.1f, 0.9f);
        public Color quitButtonColor = new Color(0.7f, 0.1f, 0.1f, 0.9f);
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

            if (coverSprite == null)
            {
                coverSprite = Resources.Load<Sprite>("Menus/Sprites/main_menu");
                if (coverSprite == null)
                    coverSprite = Resources.Load<Sprite>("main_menu");
            }

            SetupUI();
        }

        private void SetupUI()
        {
            GameObject canvasObj = new GameObject("MainMenuCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            CreateAdaptiveImage(canvasObj.transform, "DarkOverlay", new Vector2(0.5f, 0.5f), new Vector2(1.2f, 1.2f), new Color(0f, 0f, 0f, 0.5f), null);

            if (coverSprite != null)
                CreateAdaptiveImage(canvasObj.transform, "CentralCoverImage", new Vector2(0.5f, 0.52f), new Vector2(0.38f, 0.48f), Color.white, coverSprite);

            CreateAdaptiveText(canvasObj.transform, "XENOBOT", new Vector2(0.5f, 0.88f), titleColor, true);

            CreateAdaptiveButton(canvasObj.transform, "SETTINGS", new Vector2(0.2f, 0.6f), OpenSettings, buttonsColor);
            CreateAdaptiveButton(canvasObj.transform, "LEVELS", new Vector2(0.2f, 0.4f), OpenLevels, buttonsColor);
            CreateAdaptiveButton(canvasObj.transform, "CREDITS", new Vector2(0.8f, 0.6f), OpenCredits, buttonsColor);
            CreateAdaptiveButton(canvasObj.transform, "QUIT", new Vector2(0.8f, 0.4f), Quit, quitButtonColor);
            CreateAdaptiveButton(canvasObj.transform, "PLAY", new Vector2(0.5f, 0.18f), Play, playButtonColor);
        }

        private void CreateAdaptiveText(Transform parent, string content, Vector2 anchorPos, Color color, bool isUpper)
        {
            GameObject textObj = new GameObject("TitleText");
            textObj.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = isUpper ? content.ToUpper() : content;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 20;
            tmp.fontSizeMax = 250;

            RectTransform rect = textObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorPos.x - 0.4f, anchorPos.y - 0.08f);
            rect.anchorMax = new Vector2(anchorPos.x + 0.4f, anchorPos.y + 0.08f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition3D = Vector3.zero;
            rect.localScale = Vector3.one;
        }

        private void CreateAdaptiveImage(Transform parent, string name, Vector2 anchorPos, Vector2 sizeRatio, Color color, Sprite sprite)
        {
            GameObject imgObj = new GameObject(name);
            imgObj.transform.SetParent(parent, false);
            Image img = imgObj.AddComponent<Image>();
            img.color = color;

            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
            }

            RectTransform rect = imgObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorPos.x - (sizeRatio.x / 2f), anchorPos.y - (sizeRatio.y / 2f));
            rect.anchorMax = new Vector2(anchorPos.x + (sizeRatio.x / 2f), anchorPos.y + (sizeRatio.y / 2f));
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition3D = Vector3.zero;
            rect.localScale = Vector3.one;
        }

        private void CreateAdaptiveButton(Transform parent, string label, Vector2 anchorPos, UnityEngine.Events.UnityAction action, Color bgColor)
        {
            GameObject btnObj = new GameObject("Button_" + label);
            btnObj.transform.SetParent(parent, false);

            Image img = btnObj.AddComponent<Image>();
            img.color = bgColor;

            Button btn = btnObj.AddComponent<Button>();
            btn.onClick.AddListener(action);

            RectTransform rect = btnObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorPos.x - 0.09f, anchorPos.y - 0.045f);
            rect.anchorMax = new Vector2(anchorPos.x + 0.09f, anchorPos.y + 0.045f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition3D = Vector3.zero;
            rect.localScale = Vector3.one;

            GameObject txtObj = new GameObject("Label");
            txtObj.transform.SetParent(btnObj.transform, false);
            TextMeshProUGUI tmp = txtObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.color = buttonTextColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 14;
            tmp.fontSizeMax = 60;

            RectTransform txtRect = txtObj.GetComponent<RectTransform>();
            txtRect.anchorMin = new Vector2(0.1f, 0.1f);
            txtRect.anchorMax = new Vector2(0.9f, 0.9f);
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;
            txtRect.anchoredPosition3D = Vector3.zero;
            txtRect.localScale = Vector3.one;
        }

        public void Play() => LoadScene(gameScene);
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