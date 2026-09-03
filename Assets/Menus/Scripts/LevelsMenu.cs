using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using Unity.Netcode;
using Levels.Data;

public class LevelsMenu : MonoBehaviour
{
    [Header("Configuración de Escenas")]
    public string escenaMenuPrincipal = "MainMenuScene";

    [Header("Datos de Niveles")]
    public static List<LevelData> listaNiveles = new List<LevelData>();
    public List<LevelData> nivelesConfig = new List<LevelData>();
    public List<Sprite> fallbackIcons = new List<Sprite>();

    public static float ultimoTiempoSession = 0f;
    public static string ultimoNivelSession = "";

    public static string FormatTime(float t) => string.Format("{0:00}:{1:00}", Mathf.FloorToInt(t / 60), Mathf.FloorToInt(t % 60));

    [Header("Estilo Visual Xenobot")]
    public Color colorFondo = new Color(0.01f, 0.02f, 0.05f, 1f);
    public Color colorCard = new Color(0.05f, 0.1f, 0.2f, 0.8f);
    public Color colorAccent = new Color(0f, 1f, 1f, 1f);
    public Color colorTexto = Color.white;
    public Color colorBorde = new Color(0f, 1f, 1f, 0.3f);

    private RectTransform contentTransform;

    private void Start()
    {
        // Forzar visibilidad del mouse
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (listaNiveles.Count == 0 && nivelesConfig.Count > 0)
        {
            listaNiveles.AddRange(nivelesConfig);
        }
        SetupUI();
    }

    private void SetupUI()
    {
        // 1. Canvas Adaptativo
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

        // 2. Fondo
        CreateAdaptiveImage(canvasObj.transform, "Background", new Vector2(0.5f, 0.5f), new Vector2(1.1f, 1.1f), colorFondo);

        // 3. Título de Escena
        CreateSubText(canvasObj.transform, "SELECCIÓN DE NIVELES", new Vector2(0.1f, 0.88f), new Vector2(0.9f, 0.98f), TextAlignmentOptions.Center, colorAccent, 65);

        // 4. Sistema de Scroll Horizontal (Carrusel)
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

        // Máscara para que solo se vea dentro del área central
        scrollObj.AddComponent<RectMask2D>();

        // Contenedor de las tarjetas (Content)
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(scrollObj.transform);
        contentObj.layer = uiLayer;
        contentTransform = contentObj.AddComponent<RectTransform>();
        contentTransform.anchorMin = new Vector2(0, 0);
        contentTransform.anchorMax = new Vector2(0, 1);
        contentTransform.pivot = new Vector2(0, 0.5f);
        contentTransform.offsetMin = contentTransform.offsetMax = Vector2.zero;

        // Layout Horizontal
        HorizontalLayoutGroup layout = contentObj.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 60;
        layout.padding = new RectOffset(60, 60, 40, 40);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = true;

        ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = contentTransform;

        // 5. Generar Tarjetas de Nivel dinámicamente
        if (listaNiveles.Count == 0)
        {
            // Fallback si no hay niveles configurados
            LevelData dummy = ScriptableObject.CreateInstance<LevelData>();
            dummy.nombreNivel = "NIVEL ALPHA";
            dummy.mejorTiempo = "00:00";
            dummy.jugadoresCompletados = new List<string>{"NADIE"};
            CreateLevelCard(contentObj.transform, dummy, 0);
        }
        else
        {
            for (int i = 0; i < listaNiveles.Count; i++)
            {
                CreateLevelCard(contentObj.transform, listaNiveles[i], i);
            }
        }

        // 6. Botón Volver
        CreateAdaptiveButton(canvasObj.transform, "VOLVER AL MENÚ", new Vector2(0.5f, 0.1f), VolverAlMenu, new Color(0.8f, 0.2f, 0.2f, 0.7f));

        // EventSystem
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
    }

    private void CreateLevelCard(Transform parent, LevelData data, int index)
    {
        GameObject card = new GameObject("Card_" + data.nombreNivel);
        card.transform.SetParent(parent);
        card.layer = LayerMask.NameToLayer("UI");
        RectTransform rt = card.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(480, 0); // Ancho fijo de la tarjeta

        card.AddComponent<Image>().color = colorCard;
        CreateOutline(card.transform, colorBorde);

        VerticalLayoutGroup vLayout = card.AddComponent<VerticalLayoutGroup>();
        vLayout.padding = new RectOffset(25, 25, 25, 25);
        vLayout.spacing = 20;
        vLayout.childAlignment = TextAnchor.UpperCenter;
        vLayout.childForceExpandHeight = false;

        // 1. Miniatura (Fallback if no sprite)
        GameObject imgObj = new GameObject("Thumbnail");
        imgObj.transform.SetParent(card.transform);
        Image img = imgObj.AddComponent<Image>();

        Sprite iconToShow = data.miniatura;
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
        CreateOutline(imgObj.transform, colorBorde);

        // 2. Nombre del Nivel
        CreateSubText(card.transform, data.nombreNivel.ToUpper(), Vector2.zero, Vector2.zero, TextAlignmentOptions.Center, colorAccent, 38);

        // 3. Estadísticas
        string stats = $"<color=#00FFFF>SPEEDRUN:</color> {data.mejorTiempo}\n\n";
        stats += "<color=#00FFFF>TOP PLAYERS:</color>\n";
        if (data.jugadoresCompletados != null)
        {
            foreach (var player in data.jugadoresCompletados)
            {
                stats += $" • {player}\n";
            }
        }

        GameObject statsObj = new GameObject("StatsBox");
        statsObj.transform.SetParent(card.transform);
        TextMeshProUGUI tmp = statsObj.AddComponent<TextMeshProUGUI>();
        tmp.text = stats;
        tmp.color = colorTexto;
        tmp.fontSize = 24;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.lineSpacing = 10;
    }

    // --- MÉTODOS HELPERS ---

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
        CreateOutline(b.transform, colorBorde);
        RectTransform r = b.GetComponent<RectTransform>();
        r.anchorMin = new Vector2(anchorPos.x - 0.12f, anchorPos.y - 0.04f);
        r.anchorMax = new Vector2(anchorPos.x + 0.12f, anchorPos.y + 0.04f);
        r.offsetMin = r.offsetMax = Vector2.zero;
        r.anchoredPosition3D = Vector3.zero; r.localScale = Vector3.one;
        CreateSubText(b.transform, label, Vector2.zero, Vector2.one, TextAlignmentOptions.Center, Color.white, 30);
    }

    private void CreateOutline(Transform p, Color c)
    {
        CreateAdaptiveLine(p, new Vector2(0, 1), new Vector2(1, 1), c); // Top
        CreateAdaptiveLine(p, new Vector2(0, 0), new Vector2(1, 0), c); // Bottom
        CreateAdaptiveLine(p, new Vector2(0, 0), new Vector2(0, 1), c); // Left
        CreateAdaptiveLine(p, new Vector2(1, 0), new Vector2(1, 1), c); // Right
    }

    private void CreateAdaptiveLine(Transform p, Vector2 min, Vector2 max, Color c)
    {
        GameObject l = new GameObject("Line");
        l.transform.SetParent(p);
        l.layer = LayerMask.NameToLayer("UI");
        l.AddComponent<Image>().color = c;
        RectTransform r = l.GetComponent<RectTransform>();
        r.anchorMin = min; r.anchorMax = max;
        r.offsetMin = new Vector2(-1,-1); r.offsetMax = new Vector2(1,1);
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

    public void VolverAlMenu()
    {
        var allNMs = Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
        foreach (var nm in allNMs)
        {
            if (nm.IsListening) nm.Shutdown();
            Destroy(nm.gameObject);
        }
        SceneManager.LoadScene(escenaMenuPrincipal);
    }
}
