using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// Vida del player. Server-authoritative (mismo patr�n que enemyHealth.cs).
// Genera toda la UI (barra de vida + barra de jetpack) por c�digo, sin depender
// de ning�n prefab de HUD. Solo se muestra para el Owner.
public class PlayerHealth : NetworkBehaviour
{
    [Header("Vida")]
    public int maxHealth = 100;

    [Header("Jetpack")]
    public float maxJetpack = 100f;

    [Header("Colores")]
    public Color healthColor = new Color(0.85f, 0.1f, 0.1f);
    public Color jetpackColor = new Color(0.1f, 0.85f, 0.85f);
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.6f);
    public Color flashFullColor = Color.white;
    public Color flashEmptyColor = new Color(1f, 0.3f, 0.3f);

    NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    int m_OfflineHealth;
    float m_Jetpack;

    Image m_HealthFill, m_HealthBg, m_JetpackFill, m_JetpackBg;
    FillBarColorChange m_HealthColorChange, m_JetpackColorChange;

    void Awake()
    {
        m_OfflineHealth = maxHealth;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) currentHealth.Value = maxHealth;
        if (IsOwner)
        {
            m_Jetpack = maxJetpack;
            BuildHud();
        }
    }

    [Rpc(SendTo.Server)]
    public void ApplyDamageRpc(int damage)
    {
        ApplyNetworkDamage(damage);
    }

    public void TakeDamage(int damage)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            if (IsServer)
                ApplyNetworkDamage(damage);
            else
                ApplyDamageRpc(damage);

            return;
        }

        ApplyOfflineDamage(damage);
    }

    void ApplyNetworkDamage(int damage)
    {
        if (!IsServer || currentHealth.Value <= 0) return;

        currentHealth.Value -= damage;
        Debug.Log(gameObject.name + " (player) recibi� " + damage + " de da�o");

        if (currentHealth.Value <= 0)
            Debug.Log(gameObject.name + " muri�"); // hook para futura l�gica de muerte/respawn
    }

    void ApplyOfflineDamage(int damage)
    {
        if (m_OfflineHealth <= 0) return;

        m_OfflineHealth -= damage;
        Debug.Log(gameObject.name + " (player) recibi� " + damage + " de da�o");

        if (m_OfflineHealth <= 0)
            Debug.Log(gameObject.name + " muri�");
    }

    void Update()
    {
        if (!IsOwner || m_HealthFill == null) return;

        // Vida
        int displayedHealth = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned
            ? currentHealth.Value
            : m_OfflineHealth;
        float healthRatio = (float)displayedHealth / maxHealth;
        m_HealthFill.fillAmount = healthRatio;
        m_HealthColorChange.UpdateVisual(healthRatio);

        // Jetpack: la barra refleja m_Jetpack, que se actualiza v�a SetJetpackRatio().
        // No existe (todav�a) un sistema de jetpack en el proyecto; mientras tanto queda llena.
        float jetpackRatio = m_Jetpack / maxJetpack;
        m_JetpackFill.fillAmount = jetpackRatio;
        m_JetpackColorChange.UpdateVisual(jetpackRatio);
    }

    // Hook p�blico: cuando exista el script de jetpack, llamar aqu� con el ratio actual (0 a 1).
    public void SetJetpackRatio(float ratio01)
    {
        m_Jetpack = Mathf.Clamp01(ratio01) * maxJetpack;
    }

    // ---------- Construcci�n de la UI por c�digo ----------

    void BuildHud()
    {
        var canvasGo = new GameObject("PlayerHealthHUD");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        canvasGo.AddComponent<CanvasScaler>();

        // Barra de jetpack (arriba), barra de vida (abajo) � esquina inferior izquierda
        m_JetpackBg = CreateBar(canvasGo.transform, "JetpackBar", jetpackColor, backgroundColor,
            new Vector2(20, 20), out m_JetpackFill);
        m_HealthBg = CreateBar(canvasGo.transform, "HealthBar", healthColor, backgroundColor,
            new Vector2(20, 50), out m_HealthFill, withIcon: true);

        m_JetpackColorChange = m_JetpackBg.gameObject.AddComponent<FillBarColorChange>();
        SetupColorChange(m_JetpackColorChange, m_JetpackFill, m_JetpackBg, jetpackColor);

        m_HealthColorChange = m_HealthBg.gameObject.AddComponent<FillBarColorChange>();
        SetupColorChange(m_HealthColorChange, m_HealthFill, m_HealthBg, healthColor);
    }

    void SetupColorChange(FillBarColorChange fbc, Image fill, Image bg, Color defaultColor)
    {
        fbc.ForegroundImage = fill;
        fbc.BackgroundImage = bg;
        fbc.DefaultForegroundColor = defaultColor;
        fbc.DefaultBackgroundColor = backgroundColor;
        fbc.FlashForegroundColorFull = flashFullColor;
        fbc.FlashBackgroundColorEmpty = flashEmptyColor;
        fbc.Initialize(1f, 0f);
    }

    Image CreateBar(Transform parent, string name, Color fillColor, Color bgColor, Vector2 anchoredPos,
        out Image fillImage, bool withIcon = false)
    {
        float width = 220f, height = 24f;
        float iconSize = withIcon ? height : 0f;

        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = Vector2.zero; // esquina inferior izquierda
        rootRect.pivot = new Vector2(0f, 0f);
        rootRect.sizeDelta = new Vector2(width + iconSize, height);
        rootRect.anchoredPosition = anchoredPos;

        // Fondo
        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(root.transform, false);
        var bgImg = bgGo.AddComponent<Image>();
        bgImg.sprite = SolidSprite();
        bgImg.type = Image.Type.Simple;
        bgImg.color = bgColor;
        var bgRect = bgImg.rectTransform;
        bgRect.anchorMin = new Vector2(0f, 0f);
        bgRect.anchorMax = new Vector2(0f, 1f);
        bgRect.pivot = new Vector2(0f, 0.5f);
        bgRect.anchoredPosition = new Vector2(iconSize, 0f);
        bgRect.sizeDelta = new Vector2(width, 0f);

        // Fill
        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(bgGo.transform, false);
        fillImage = fillGo.AddComponent<Image>();
        fillImage.sprite = SolidSprite();
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.color = fillColor;
        var fillRect = fillImage.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        // �cono (solo barra de vida): cruz blanca sobre cuadrado de color
        if (withIcon)
        {
            var iconBgGo = new GameObject("HealthIcon", typeof(RectTransform));
            iconBgGo.transform.SetParent(root.transform, false);
            var iconBg = iconBgGo.AddComponent<Image>();
            iconBg.sprite = SolidSprite();
            iconBg.color = fillColor;
            var iconRect = iconBg.rectTransform;
            iconRect.anchorMin = new Vector2(0f, 0f);
            iconRect.anchorMax = new Vector2(0f, 1f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(iconSize, 0f);

            var crossGo = new GameObject("Cross", typeof(RectTransform));
            crossGo.transform.SetParent(iconBgGo.transform, false);
            var crossImg = crossGo.AddComponent<Image>();
            crossImg.sprite = CrossSprite();
            crossImg.color = Color.white;
            var crossRect = crossImg.rectTransform;
            crossRect.anchorMin = Vector2.zero;
            crossRect.anchorMax = Vector2.one;
            crossRect.offsetMin = new Vector2(4, 4);
            crossRect.offsetMax = new Vector2(-4, -4);
        }

        return bgImg;
    }

    static Sprite s_SolidSprite;
    static Sprite SolidSprite()
    {
        if (s_SolidSprite != null) return s_SolidSprite;
        var tex = Texture2D.whiteTexture;
        s_SolidSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
        return s_SolidSprite;
    }

    static Sprite s_CrossSprite;
    static Sprite CrossSprite()
    {
        if (s_CrossSprite != null) return s_CrossSprite;

        int s = 16, arm = 4; // grosor del brazo de la cruz
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color[] px = new Color[s * s];
        int center = s / 2;

        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool inCross = Mathf.Abs(x - center) < arm || Mathf.Abs(y - center) < arm;
                px[y * s + x] = inCross ? Color.white : new Color(0, 0, 0, 0);
            }

        tex.SetPixels(px);
        tex.Apply();
        s_CrossSprite = Sprite.Create(tex, new Rect(0, 0, s, s), Vector2.one * 0.5f);
        return s_CrossSprite;
    }
}
