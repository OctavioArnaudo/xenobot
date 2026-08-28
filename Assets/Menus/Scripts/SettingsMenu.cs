using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class SettingsMenu : MonoBehaviour
{
    [Header("Configuración de Escenas")]
    public string escenaMenuPrincipal = "MainMenuScene";

    [Header("Estilo Visual Xenobot")]
    public Color colorFondo = new Color(0.01f, 0.02f, 0.05f, 1f);
    public Color colorSidebar = new Color(0.05f, 0.1f, 0.2f, 0.95f);
    public Color colorPanel = new Color(0.1f, 0.2f, 0.3f, 0.7f);
    public Color colorBotonActivo = new Color(0f, 0.8f, 1f, 0.6f);
    public Color colorAccent = new Color(0f, 1f, 1f, 1f);
    public Color colorTexto = Color.white;
    public Color colorBorde = new Color(0f, 1f, 1f, 0.4f);

    private GameObject mainPanelContainer;
    private List<GameObject> paneles = new List<GameObject>();
    private List<Image> imagenesBotonesSidebar = new List<Image>();

    private void Start()
    {
        SetupUI();
    }

    private void SetupUI()
    {
        // 1. Canvas Raíz con máxima prioridad
        GameObject canvasObj = new GameObject("SettingsCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer == -1) uiLayer = 5;
        canvasObj.layer = uiLayer;

        // 2. EventSystem
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // 3. Fondo
        CreateAdaptiveImage(canvasObj.transform, "Background", new Vector2(0.5f, 0.5f), new Vector2(1.5f, 1.5f), colorFondo);

        // 4. Barra Lateral (Sidebar)
        GameObject sidebar = new GameObject("Sidebar");
        sidebar.transform.SetParent(canvasObj.transform);
        sidebar.layer = uiLayer;
        RectTransform sideRT = sidebar.AddComponent<RectTransform>();
        sideRT.anchorMin = new Vector2(0, 0);
        sideRT.anchorMax = new Vector2(0.25f, 1f);
        sideRT.offsetMin = sideRT.offsetMax = Vector2.zero;
        sideRT.anchoredPosition3D = Vector3.zero;
        sideRT.localScale = Vector3.one;
        sidebar.AddComponent<Image>().color = colorSidebar;
        CreateOutline(sidebar.transform, colorBorde);

        // 5. Contenedor de Paneles
        mainPanelContainer = new GameObject("ContentContainer");
        mainPanelContainer.transform.SetParent(canvasObj.transform);
        mainPanelContainer.layer = uiLayer;
        RectTransform contRT = mainPanelContainer.AddComponent<RectTransform>();
        contRT.anchorMin = new Vector2(0.28f, 0.05f);
        contRT.anchorMax = new Vector2(0.95f, 0.95f);
        contRT.offsetMin = contRT.offsetMax = Vector2.zero;
        contRT.anchoredPosition3D = Vector3.zero;
        contRT.localScale = Vector3.one;
        mainPanelContainer.AddComponent<Image>().color = colorPanel;
        CreateOutline(mainPanelContainer.transform, colorBorde);

        // 6. Crear Contenido
        paneles.Add(SetupSonidoPanel());
        paneles.Add(SetupControlesPanel());
        paneles.Add(SetupGraficosPanel());

        // 7. Navegación
        CreateSidebarButton(sidebar.transform, "AUDIO", 0, () => ShowPanel(0));
        CreateSidebarButton(sidebar.transform, "CONTROLES", 1, () => ShowPanel(1));
        CreateSidebarButton(sidebar.transform, "GRÁFICOS", 2, () => ShowPanel(2));
        CreateSidebarButton(sidebar.transform, "VOLVER", 7, VolverAlMenu, new Color(0.8f, 0.2f, 0.2f, 0.8f));

        ShowPanel(0);
    }

    private GameObject SetupSonidoPanel()
    {
        GameObject panel = CreateBasePanel("Panel_Sonido", "SISTEMA DE AUDIO");
        CreateSettingSlider(panel.transform, "VOLUMEN MAESTRO", 0.75f, 0.8f);
        CreateSettingSlider(panel.transform, "MÚSICA", 0.60f, 0.5f);
        CreateSettingSlider(panel.transform, "EFECTOS (SFX)", 0.45f, 0.9f);
        return panel;
    }

    private GameObject SetupControlesPanel()
    {
        GameObject panel = CreateBasePanel("Panel_Controles", "CONFIG. DE INPUT");
        CreateControlLabel(panel.transform, "AVANZAR", "W", 0.75f);
        CreateControlLabel(panel.transform, "IZQUIERDA", "A", 0.65f);
        CreateControlLabel(panel.transform, "RETROCEDER", "S", 0.55f);
        CreateControlLabel(panel.transform, "DERECHA", "D", 0.45f);
        CreateControlLabel(panel.transform, "SALTAR", "ESPACIO", 0.35f);
        return panel;
    }

    private GameObject SetupGraficosPanel()
    {
        GameObject panel = CreateBasePanel("Panel_Graficos", "VISUALES");
        CreateSettingToggle(panel.transform, "PANTALLA COMPLETA", true, 0.75f);
        CreateSettingToggle(panel.transform, "V-SYNC", false, 0.65f);
        CreateSettingToggle(panel.transform, "SOMBRAS HQ", true, 0.55f);
        return panel;
    }

    private GameObject CreateBasePanel(string name, string title)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(mainPanelContainer.transform);
        panel.layer = LayerMask.NameToLayer("UI");
        RectTransform rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.anchoredPosition3D = Vector3.zero; rt.localScale = Vector3.one;

        CreateSubText(panel.transform, title, new Vector2(0.05f, 0.85f), new Vector2(0.95f, 0.98f), TextAlignmentOptions.Left, colorAccent, 50);
        return panel;
    }

    private void CreateSettingSlider(Transform parent, string labelText, float yAnchor, float defaultValue)
    {
        GameObject container = new GameObject("SliderGroup_" + labelText);
        container.transform.SetParent(parent);
        container.layer = LayerMask.NameToLayer("UI");
        RectTransform rt = container.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.05f, yAnchor - 0.05f);
        rt.anchorMax = new Vector2(0.95f, yAnchor + 0.05f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.anchoredPosition3D = Vector3.zero; rt.localScale = Vector3.one;

        CreateSubText(container.transform, labelText, new Vector2(0, 0), new Vector2(0.4f, 1), TextAlignmentOptions.Left, colorTexto, 32);

        GameObject slideObj = new GameObject("Slider");
        slideObj.transform.SetParent(container.transform);
        Slider slider = slideObj.AddComponent<Slider>();
        RectTransform slideRT = slideObj.GetComponent<RectTransform>();
        slideRT.anchorMin = new Vector2(0.45f, 0.3f); slideRT.anchorMax = new Vector2(0.95f, 0.7f);
        slideRT.offsetMin = slideRT.offsetMax = Vector2.zero;
        slideRT.anchoredPosition3D = Vector3.zero; slideRT.localScale = Vector3.one;

        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(slideObj.transform);
        bg.AddComponent<Image>().color = Color.black;
        CreateOutline(bg.transform, colorBorde);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
        bgRT.anchoredPosition3D = Vector3.zero; bgRT.localScale = Vector3.one;

        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(slideObj.transform);
        RectTransform fillAreaRT = fillArea.AddComponent<RectTransform>();
        fillAreaRT.anchorMin = Vector2.zero; fillAreaRT.anchorMax = Vector2.one;
        fillAreaRT.offsetMin = new Vector2(5, 5); fillAreaRT.offsetMax = new Vector2(-5, -5);
        fillAreaRT.anchoredPosition3D = Vector3.zero; fillAreaRT.localScale = Vector3.one;

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform);
        fill.AddComponent<Image>().color = colorAccent;
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.fillRect.offsetMin = slider.fillRect.offsetMax = Vector2.zero;
        slider.fillRect.anchoredPosition3D = Vector3.zero; slider.fillRect.localScale = Vector3.one;

        slider.value = defaultValue;
    }

    private void CreateControlLabel(Transform parent, string action, string key, float yAnchor)
    {
        GameObject container = new GameObject("Control_" + action);
        container.transform.SetParent(parent);
        container.layer = LayerMask.NameToLayer("UI");
        RectTransform rt = container.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.05f, yAnchor - 0.04f);
        rt.anchorMax = new Vector2(0.95f, yAnchor + 0.04f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.anchoredPosition3D = Vector3.zero; rt.localScale = Vector3.one;

        CreateSubText(container.transform, action, new Vector2(0, 0), new Vector2(0.5f, 1), TextAlignmentOptions.Left, colorTexto, 30);
        CreateSubText(container.transform, key, new Vector2(0.55f, 0), new Vector2(1f, 1), TextAlignmentOptions.Right, colorAccent, 30);
    }

    private void CreateSettingToggle(Transform parent, string labelText, bool initialState, float yAnchor)
    {
        GameObject container = new GameObject("Toggle_" + labelText);
        container.transform.SetParent(parent);
        container.layer = LayerMask.NameToLayer("UI");
        RectTransform rt = container.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.05f, yAnchor - 0.05f);
        rt.anchorMax = new Vector2(0.95f, yAnchor + 0.05f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.anchoredPosition3D = Vector3.zero; rt.localScale = Vector3.one;

        CreateSubText(container.transform, labelText, new Vector2(0, 0), new Vector2(0.7f, 1), TextAlignmentOptions.Left, colorTexto, 30);

        GameObject box = new GameObject("ToggleBox");
        box.transform.SetParent(container.transform);
        box.AddComponent<Image>().color = new Color(0,0,0,0.5f);
        CreateOutline(box.transform, colorBorde);
        RectTransform boxRT = box.GetComponent<RectTransform>();
        boxRT.anchorMin = new Vector2(0.85f, 0.1f); boxRT.anchorMax = new Vector2(0.95f, 0.9f);
        boxRT.offsetMin = boxRT.offsetMax = Vector2.zero;
        boxRT.anchoredPosition3D = Vector3.zero; boxRT.localScale = Vector3.one;

        GameObject check = new GameObject("Checkmark");
        check.transform.SetParent(box.transform);
        check.AddComponent<Image>().color = colorAccent;
        RectTransform checkRT = check.GetComponent<RectTransform>();
        checkRT.anchorMin = new Vector2(0.2f,0.2f); checkRT.anchorMax = new Vector2(0.8f,0.8f);
        checkRT.offsetMin = checkRT.offsetMax = Vector2.zero;
        checkRT.anchoredPosition3D = Vector3.zero; checkRT.localScale = Vector3.one;
        check.SetActive(initialState);

        box.AddComponent<Button>().onClick.AddListener(() => {
            initialState = !initialState;
            check.SetActive(initialState);
        });
    }

    private void CreateSubText(Transform p, string c, Vector2 min, Vector2 max, TextAlignmentOptions al, Color col, float size = 24)
    {
        GameObject obj = new GameObject("Text_" + c);
        obj.transform.SetParent(p);
        obj.layer = LayerMask.NameToLayer("UI");

        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.anchoredPosition3D = Vector3.zero;
        rt.localScale = Vector3.one;

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = c;
        tmp.color = col;
        tmp.alignment = al;
        tmp.fontSize = size;

        // --- CONFIGURACIÓN DE VISIBILIDAD CRÍTICA ---
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 12;
        tmp.fontSizeMax = size * 1.5f;
        tmp.enableWordWrapping = false; // MUY IMPORTANTE: Evita que el texto desaparezca al intentar envolver
        tmp.overflowMode = TextOverflowModes.Overflow; // Asegura que se vea aunque el rect sea pequeño
        tmp.raycastTarget = false;
    }

    private void CreateSidebarButton(Transform parent, string label, int pos, UnityEngine.Events.UnityAction act, Color? bg = null)
    {
        GameObject b = new GameObject("Btn_" + label);
        b.transform.SetParent(parent);
        b.layer = LayerMask.NameToLayer("UI");
        Image i = b.AddComponent<Image>();
        i.color = bg ?? new Color(0, 0, 0, 0);
        if (bg == null) imagenesBotonesSidebar.Add(i);
        b.AddComponent<Button>().onClick.AddListener(act);
        RectTransform r = b.GetComponent<RectTransform>();
        float y = 0.88f - (pos * 0.12f);
        r.anchorMin = new Vector2(0.05f, y - 0.05f); r.anchorMax = new Vector2(0.95f, y + 0.05f);
        r.offsetMin = r.offsetMax = Vector2.zero;
        r.anchoredPosition3D = Vector3.zero; r.localScale = Vector3.one;
        CreateSubText(b.transform, label, Vector2.zero, Vector2.one, TextAlignmentOptions.Center, colorTexto, 35);
        if (bg == null) CreateOutline(b.transform, colorBorde);
    }

    public void ShowPanel(int idx)
    {
        for (int i = 0; i < paneles.Count; i++) {
            paneles[i].SetActive(i == idx);
            if (i < imagenesBotonesSidebar.Count)
                imagenesBotonesSidebar[i].color = (i == idx) ? colorBotonActivo : new Color(0, 0, 0, 0);
        }
    }

    private void CreateOutline(Transform p, Color c)
    {
        CreateAdaptiveImage(p, "T", new Vector2(0.5f, 1f), new Vector2(1f, 0.005f), c);
        CreateAdaptiveImage(p, "B", new Vector2(0.5f, 0f), new Vector2(1f, 0.005f), c);
        CreateAdaptiveImage(p, "L", new Vector2(0f, 0.5f), new Vector2(0.005f, 1f), c);
        CreateAdaptiveImage(p, "R", new Vector2(1f, 0.5f), new Vector2(0.005f, 1f), c);
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

    public void VolverAlMenu() => SceneManager.LoadScene(escenaMenuPrincipal);
}
