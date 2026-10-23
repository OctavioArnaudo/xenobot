using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

namespace Runtime.Scripts.Credits
{
    public class CreditsMenu : MonoBehaviour
    {
        [Header("Scene Configuration")]
        public string mainScene = "MainScene";

        [Header("Credits Data")]
        public string titleText = "CREDITS";
        [TextArea(4, 10)]
        public string developersList = "DEVELOPERS\n\nJoaquin Luna\nJeronimo Cortez Cabral\nJuan Pablo Garay\nOctavio Arnaudo";

        [Header("Visual Style & Resources")]
        public Sprite creditsSprite;
        public Color titleColor = Color.cyan;
        public Color namesColor = Color.white;
        public Color backButtonColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        public Color buttonTextColor = Color.white;

        private void Start()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (creditsSprite == null)
            {
                creditsSprite = Resources.Load<Sprite>("Menus/Sprites/credits_menu");
                if (creditsSprite == null)
                    creditsSprite = Resources.Load<Sprite>("credits_menu");
            }

            SetupUI();
        }

        private void SetupUI()
        {
            GameObject canvasObj = new GameObject("CreditsCanvas");
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

            CreateAdaptiveImage(canvasObj.transform, "DarkOverlay", new Vector2(0.5f, 0.5f), new Vector2(1.2f, 1.2f), new Color(0f, 0f, 0f, 0.75f), null);

            if (creditsSprite != null)
                CreateAdaptiveImage(canvasObj.transform, "CreditsImage", new Vector2(0.28f, 0.5f), new Vector2(0.35f, 0.55f), Color.white, creditsSprite);

            Vector2 contentPos = creditsSprite != null ? new Vector2(0.65f, 0.5f) : new Vector2(0.5f, 0.5f);
            Vector2 titlePos = creditsSprite != null ? new Vector2(0.65f, 0.85f) : new Vector2(0.5f, 0.85f);
            Vector2 buttonPos = creditsSprite != null ? new Vector2(0.65f, 0.15f) : new Vector2(0.5f, 0.15f);

            CreateAdaptiveText(canvasObj.transform, titleText, titlePos, 0.12f, titleColor, true);
            CreateAdaptiveText(canvasObj.transform, developersList, contentPos, 0.45f, namesColor, false);
            CreateAdaptiveButton(canvasObj.transform, "BACK", buttonPos, ReturnToMenu, backButtonColor);
        }

        private void CreateAdaptiveText(Transform parent, string content, Vector2 anchorPos, float heightRatio, Color color, bool isUpper)
        {
            GameObject textObj = new GameObject("Text_" + (isUpper ? "Title" : "Content"));
            textObj.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = isUpper ? content.ToUpper() : content;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.lineSpacing = 15f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 16;
            tmp.fontSizeMax = 120;

            RectTransform rect = textObj.GetComponent<RectTransform>();
            float widthRatio = isUpper ? 0.8f : 0.5f;
            rect.anchorMin = new Vector2(anchorPos.x - (widthRatio / 2f), anchorPos.y - (heightRatio / 2f));
            rect.anchorMax = new Vector2(anchorPos.x + (widthRatio / 2f), anchorPos.y + (heightRatio / 2f));
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
            rect.anchorMin = new Vector2(anchorPos.x - 0.08f, anchorPos.y - 0.04f);
            rect.anchorMax = new Vector2(anchorPos.x + 0.08f, anchorPos.y + 0.04f);
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

        public void ReturnToMenu()
        {
            if (!string.IsNullOrEmpty(mainScene))
                SceneManager.LoadScene(mainScene);
        }
    }
}