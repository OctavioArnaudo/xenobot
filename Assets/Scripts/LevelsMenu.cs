using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using Unity.Netcode;

namespace Runtime.Scripts.Levels
{
    public class LevelsMenu : MonoBehaviour
    {
        [Header("Scene Configuration")]
        public string mainMenuScene = "MainMenuScene";

        [Header("Level Data")]
        public static List<LevelsData> levelsList = new List<LevelsData>();
        public List<LevelsData> levelsConfig = new List<LevelsData>();
        public List<Sprite> fallbackIcons = new List<Sprite>();

        public static float lastSessionTime = 0f;
        public static string lastSessionLevel = "";

        public static string FormatTime(float t) => string.Format("{0:00}:{1:00}", Mathf.FloorToInt(t / 60), Mathf.FloorToInt(t % 60));

        [Header("Xenobot Visual Style")]
        public Color backgroundColor = new Color(0.01f, 0.02f, 0.05f, 1f);
        public Color cardColor = new Color(0.05f, 0.1f, 0.2f, 0.8f);
        public Color accentColor = new Color(0f, 1f, 1f, 1f);
        public Color textColor = Color.white;
        public Color borderColor = new Color(0f, 1f, 1f, 0.3f);

        private RectTransform _contentTransform;

        private void Start()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (levelsList.Count == 0 && levelsConfig.Count > 0)
            {
                levelsList.AddRange(levelsConfig);
            }
            SetupUI();
        }

        private void SetupUI()
        {
            GameObject canvasObj = new GameObject("LevelsCanvas");
            canvasObj.transform.SetParent(this.transform);
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer == -1) uiLayer = 5;
            canvasObj.layer = uiLayer;

            CreateAdaptiveImage(canvasObj.transform, "Background", new Vector2(0.5f, 0.5f), new Vector2(1.1f, 1.1f), backgroundColor);

            CreateSubText(canvasObj.transform, "LEVEL SELECT", new Vector2(0.1f, 0.88f), new Vector2(0.9f, 0.98f), TextAlignmentOptions.Center, accentColor, 65);

            GameObject scrollObj = new GameObject("LevelScrollView");
            scrollObj.transform.SetParent(canvasObj.transform);
            scrollObj.layer = uiLayer;
            RectTransform scrollRT = scrollObj.AddComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0.05f, 0.22f);
            scrollRT.anchorMax = new Vector2(0.95f, 0.82f);
            scrollRT.offsetMin = scrollRT.offsetMax = Vector2.zero;

            ScrollRect scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.horizontal = true;
            scrollRect.vertical = false;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 25;

            scrollObj.AddComponent<RectMask2D>();

            GameObject contentObj = new GameObject("Content");
            contentObj.transform.SetParent(scrollObj.transform);
            contentObj.layer = uiLayer;
            _contentTransform = contentObj.AddComponent<RectTransform>();
            _contentTransform.anchorMin = new Vector2(0, 0);
            _contentTransform.anchorMax = new Vector2(0, 1);
            _contentTransform.pivot = new Vector2(0, 0.5f);
            _contentTransform.offsetMin = _contentTransform.offsetMax = Vector2.zero;

            HorizontalLayoutGroup layout = contentObj.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 60;
            layout.padding = new RectOffset(60, 60, 40, 40);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;

            ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = _contentTransform;

            if (levelsList.Count == 0)
            {
                LevelsData dummy = ScriptableObject.CreateInstance<LevelsData>();
                dummy.levelName = "LEVEL ALPHA";
                dummy.bestTime = "00:00";
                dummy.completedPlayers = new List<string> { "NONE" };
                CreateLevelCard(contentObj.transform, dummy, 0);
            }
            else
            {
                for (int i = 0; i < levelsList.Count; i++)
                {
                    CreateLevelCard(contentObj.transform, levelsList[i], i);
                }
            }

            CreateAdaptiveButton(canvasObj.transform, "MAIN MENU", new Vector2(0.5f, 0.1f), ReturnToMenu, new Color(0.8f, 0.2f, 0.2f, 0.7f));

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }

        private void CreateLevelCard(Transform parent, LevelsData data, int index)
        {
            GameObject card = new GameObject("Card_" + data.levelName);
            card.transform.SetParent(parent);
            card.layer = LayerMask.NameToLayer("UI");
            RectTransform rt = card.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(480, 0);

            card.AddComponent<Image>().color = cardColor;
            CreateOutline(card.transform, borderColor);

            VerticalLayoutGroup vLayout = card.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(25, 25, 25, 25);
            vLayout.spacing = 20;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childForceExpandHeight = false;

            GameObject imgObj = new GameObject("Thumbnail");
            imgObj.transform.SetParent(card.transform);
            Image img = imgObj.AddComponent<Image>();

            Sprite iconToShow = data.thumbnail;
            if (iconToShow == null && fallbackIcons.Count > 0)
            {
                iconToShow = fallbackIcons[index % fallbackIcons.Count];
            }

            img.sprite = iconToShow;
            img.color = (iconToShow == null) ? new Color(0.2f, 0.2f, 0.2f, 1f) : Color.white;
            img.preserveAspect = true;

            LayoutElement le = imgObj.AddComponent<LayoutElement>();
            le.preferredHeight = 280;
            le.flexibleHeight = 0;
            CreateOutline(imgObj.transform, borderColor);

            CreateSubText(card.transform, data.levelName.ToUpper(), Vector2.zero, Vector2.zero, TextAlignmentOptions.Center, accentColor, 38);

            string stats = $"<color=#00FFFF>SPEEDRUN:</color> {data.bestTime}\n\n";
            stats += "<color=#00FFFF>TOP PLAYERS:</color>\n";
            if (data.completedPlayers != null)
            {
                foreach (var player in data.completedPlayers)
                {
                    stats += $" • {player}\n";
                }
            }

            GameObject statsObj = new GameObject("StatsBox");
            statsObj.transform.SetParent(card.transform);
            TextMeshProUGUI tmp = statsObj.AddComponent<TextMeshProUGUI>();
            tmp.text = stats;
            tmp.color = textColor;
            tmp.fontSize = 24;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.lineSpacing = 10;
        }

        private void CreateSubText(Transform p, string c, Vector2 min, Vector2 max, TextAlignmentOptions al, Color col, float size)
        {
            GameObject obj = new GameObject("Text_" + c);
            obj.transform.SetParent(p);
            obj.layer = LayerMask.NameToLayer("UI");
            TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = c; tmp.color = col; tmp.alignment = al; tmp.fontSize = size;
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 12;
            RectTransform rt = obj.GetComponent<RectTransform>();
            if (min != Vector2.zero || max != Vector2.zero)
            {
                rt.anchorMin = min; rt.anchorMax = max;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            rt.anchoredPosition3D = Vector3.zero; rt.localScale = Vector3.one;
        }

        private void CreateAdaptiveButton(Transform parent, string label, Vector2 anchorPos, UnityEngine.Events.UnityAction act, Color bg)
        {
            GameObject b = new GameObject("Btn_" + label);
            b.transform.SetParent(parent);
            b.layer = LayerMask.NameToLayer("UI");
            b.AddComponent<Image>().color = bg;
            b.AddComponent<Button>().onClick.AddListener(act);
            CreateOutline(b.transform, borderColor);
            RectTransform r = b.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(anchorPos.x - 0.12f, anchorPos.y - 0.04f);
            r.anchorMax = new Vector2(anchorPos.x + 0.12f, anchorPos.y + 0.04f);
            r.offsetMin = r.offsetMax = Vector2.zero;
            r.anchoredPosition3D = Vector3.zero; r.localScale = Vector3.one;
            CreateSubText(b.transform, label, Vector2.zero, Vector2.one, TextAlignmentOptions.Center, Color.white, 30);
        }

        private void CreateOutline(Transform p, Color c)
        {
            CreateAdaptiveLine(p, new Vector2(0, 1), new Vector2(1, 1), c);
            CreateAdaptiveLine(p, new Vector2(0, 0), new Vector2(1, 0), c);
            CreateAdaptiveLine(p, new Vector2(0, 0), new Vector2(0, 1), c);
            CreateAdaptiveLine(p, new Vector2(1, 0), new Vector2(1, 1), c);
        }

        private void CreateAdaptiveLine(Transform p, Vector2 min, Vector2 max, Color c)
        {
            GameObject l = new GameObject("Line");
            l.transform.SetParent(p);
            l.layer = LayerMask.NameToLayer("UI");
            l.AddComponent<Image>().color = c;
            RectTransform r = l.GetComponent<RectTransform>();
            r.anchorMin = min; r.anchorMax = max;
            r.offsetMin = new Vector2(-1, -1); r.offsetMax = new Vector2(1, 1);
            r.anchoredPosition3D = Vector3.zero; r.localScale = Vector3.one;
        }

        private void CreateAdaptiveImage(Transform p, string n, Vector2 ap, Vector2 sr, Color c)
        {
            GameObject o = new GameObject(n);
            o.transform.SetParent(p);
            o.layer = LayerMask.NameToLayer("UI");
            o.AddComponent<Image>().color = c;
            RectTransform r = o.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(ap.x - (sr.x / 2), ap.y - (sr.y / 2));
            r.anchorMax = new Vector2(ap.x + (sr.x / 2), ap.y + (sr.y / 2));
            r.offsetMin = r.offsetMax = Vector2.zero;
            r.anchoredPosition3D = Vector3.zero; r.localScale = Vector3.one;
        }

        public void ReturnToMenu()
        {
            var allNMs = Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
            foreach (var nm in allNMs)
            {
                if (nm.IsListening) nm.Shutdown();
                Destroy(nm.gameObject);
            }
            SceneManager.LoadScene(mainMenuScene);
        }
    }
}