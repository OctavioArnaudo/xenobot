using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

namespace Runtime.Scripts.Credits
{
    public class CreditsMenu : MonoBehaviour
    {
        [Header("Scene Configuration")]
        public string mainMenuScene = "MainMenuScene";

        [Header("Visual Style")]
        public Color titleColor = Color.cyan;
        public Color namesColor = Color.white;
        public Color backButtonColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);

        private void Start()
        {
            SetupUI();
        }

        private void SetupUI()
        {
            GameObject canvasObj = new GameObject("CreditsCanvas");
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

            CreateAdaptiveImage(canvasObj.transform, "DarkOverlay", new Vector2(0.5f, 0.5f), new Vector2(1.2f, 1.2f), new Color(0, 0, 0, 0.7f));

            CreateAdaptiveText(canvasObj.transform, "CREDITS", new Vector2(0.5f, 0.85f), 0.15f, titleColor, true);

            string developersList = "Joaquin Luna\nJeronimo Cortez Cabral\nJuan Pablo Garay\nOctavio Arnaudo";
            CreateAdaptiveText(canvasObj.transform, developersList, new Vector2(0.5f, 0.5f), 0.4f, namesColor, false);

            CreateAdaptiveButton(canvasObj.transform, "BACK", new Vector2(0.5f, 0.15f), ReturnToMenu, backButtonColor);
        }

        private void CreateAdaptiveText(Transform parent, string content, Vector2 anchorPos, float heightRatio, Color color, bool isUpper)
        {
            GameObject textObj = new GameObject("Text_" + (isUpper ? "Title" : "Content"));
            textObj.transform.SetParent(parent);
            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = isUpper ? content.ToUpper() : content;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.lineSpacing = 20f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 18;
            tmp.fontSizeMax = 200;

            RectTransform rect = textObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.1f, anchorPos.y - (heightRatio / 2));
            rect.anchorMax = new Vector2(0.9f, anchorPos.y + (heightRatio / 2));
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
            tmp.color = Color.white;
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

        public void ReturnToMenu()
        {
            if (!string.IsNullOrEmpty(mainMenuScene))
                SceneManager.LoadScene(mainMenuScene);
            else
                Debug.LogWarning("Escena de menú no configurada.");
        }
    }
}