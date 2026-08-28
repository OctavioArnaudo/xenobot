using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class MainMenu : MonoBehaviour
{
    [Header("Configuración de Escenas")]
    public string escenaJuego = "NetworkScene";
    public string escenaNiveles = "LevelsScene";
    public string escenaCreditos = "CreditsScene";
    public string escenaAjustes = "SettingsScene";

    [Header("Referencias UI (Opcional)")]
    [Tooltip("El cover del juego. Se reseteará su posición y escala para que se vea correctamente al frente.")]
    [SerializeField] private GameObject coverImageObject;

    [Header("Estilo Visual")]
    public Color colorTitulo = Color.cyan;
    public Color colorBotones = new Color(0.15f, 0.15f, 0.15f, 0.9f);
    public Color colorTextoBotones = Color.white;

    private void Start()
    {
        SetupUI();
    }

    private void SetupUI()
    {
        // 1. Crear Canvas con alta prioridad
        GameObject canvasObj = new GameObject("MainMenuCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10; // Prioridad alta para que no quede detrás de nada

        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasObj.AddComponent<GraphicRaycaster>();

        // 2. EventSystem
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // 3. Fondo oscuro sutil para dar contraste
        CreateAdaptiveImage(canvasObj.transform, "DarkOverlay", new Vector2(0.5f, 0.5f), new Vector2(1.2f, 1.2f), new Color(0, 0, 0, 0.5f));

        // 4. Manejo del Cover Image (Reseteo de Z y Escala)
        if (coverImageObject != null)
        {
            coverImageObject.transform.SetParent(canvasObj.transform);
            RectTransform rt = coverImageObject.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            // Forzar posición al frente del canvas y escala normal
            rt.anchoredPosition3D = Vector3.zero;
            rt.localScale = Vector3.one;
        }
        else
        {
            CreateAdaptiveImage(canvasObj.transform, "CoverPlaceholder", new Vector2(0.5f, 0.5f), new Vector2(0.35f, 0.45f), new Color(0.3f, 0.3f, 0.3f, 0.8f));
        }

        // 5. Título y Botones
        CreateAdaptiveText(canvasObj.transform, "XENOBOT", new Vector2(0.5f, 0.85f), colorTitulo, true);

        CreateAdaptiveButton(canvasObj.transform, "AJUSTES", new Vector2(0.2f, 0.6f), AbrirAjustes, colorBotones);
        CreateAdaptiveButton(canvasObj.transform, "NIVELES", new Vector2(0.2f, 0.4f), AbrirNiveles, colorBotones);
        CreateAdaptiveButton(canvasObj.transform, "CREDITOS", new Vector2(0.8f, 0.6f), AbrirCreditos, colorBotones);
        CreateAdaptiveButton(canvasObj.transform, "SALIR", new Vector2(0.8f, 0.4f), Salir, new Color(0.7f, 0.1f, 0.1f));
        CreateAdaptiveButton(canvasObj.transform, "JUGAR", new Vector2(0.5f, 0.18f), Jugar, new Color(0.1f, 0.7f, 0.1f));
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
        tmp.color = colorTextoBotones;
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

    public void Jugar()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnHostIniciado;
            NetworkManager.Singleton.StartHost();
        }
        else CargarEscena(escenaJuego);
    }

    private void OnHostIniciado()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnHostIniciado;
            NetworkManager.Singleton.SceneManager.LoadScene(escenaJuego, LoadSceneMode.Single);
        }
    }

    public void AbrirNiveles() => CargarEscena(escenaNiveles);
    public void AbrirCreditos() => CargarEscena(escenaCreditos);
    public void AbrirAjustes() => CargarEscena(escenaAjustes);

    private void CargarEscena(string nombre)
    {
        if (!string.IsNullOrEmpty(nombre)) SceneManager.LoadScene(nombre);
        else Debug.LogWarning("Escena no configurada.");
    }

    public void Salir() => Application.Quit();
}
