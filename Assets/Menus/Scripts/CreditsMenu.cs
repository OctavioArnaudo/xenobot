using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class CreditsMenu : MonoBehaviour
{
    [Header("Configuración de Escenas")]
    public string escenaMenuPrincipal = "MainMenuScene";

    [Header("Estilo Visual")]
    public Color colorTitulo = Color.cyan;
    public Color colorNombres = Color.white;
    public Color colorBotonVolver = new Color(0.15f, 0.15f, 0.15f, 0.9f);

    private void Start()
    {
        SetupUI();
    }

    private void SetupUI()
    {
        // 1. Crear Canvas adaptable
        GameObject canvasObj = new GameObject("CreditsCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasObj.AddComponent<GraphicRaycaster>();

        // 2. EventSystem
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // 3. Fondo oscuro
        CreateAdaptiveImage(canvasObj.transform, "DarkOverlay", new Vector2(0.5f, 0.5f), new Vector2(1.2f, 1.2f), new Color(0, 0, 0, 0.7f));

        // 4. Título: CREDITOS
        CreateAdaptiveText(canvasObj.transform, "CREDITOS", new Vector2(0.5f, 0.85f), 0.15f, colorTitulo, true);

        // 5. Lista de Desarrolladores
        string listaDesarrolladores = "Joaquin Luna\nJeronimo Cortez Cabral\nJuan Pablo Garay\nOctavio Arnaudo";
        CreateAdaptiveText(canvasObj.transform, listaDesarrolladores, new Vector2(0.5f, 0.5f), 0.4f, colorNombres, false);

        // 6. Botón VOLVER
        CreateAdaptiveButton(canvasObj.transform, "VOLVER", new Vector2(0.5f, 0.15f), VolverAlMenu, colorBotonVolver);
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

    public void VolverAlMenu()
    {
        if (!string.IsNullOrEmpty(escenaMenuPrincipal))
            SceneManager.LoadScene(escenaMenuPrincipal);
        else
            Debug.LogWarning("Escena de menú no configurada.");
    }
}
