using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Combating.Scripts;
using Unity.Collections;
using NGO.Networking;

/// <summary>
/// Unified controller for character progression, HUD and Identity.
/// Optimized to reduce CPU overhead and audio starvation.
/// </summary>
public class StatsController : NetworkBehaviour
{
    public static StatsController Instance { get; private set; }

    [Header("Identity & Visuals")]
    public NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(new FixedString32Bytes(""), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<Color> playerColor = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public TMPro.TMP_Text nameTagText;
    public Renderer colorRenderer;

    [Header("Initial Ranges")]
    public Vector2 attackRange = new Vector2(5f, 15f);
    public Vector2 defenseRange = new Vector2(3f, 10f);

    [Header("Base Growth")]
    public float attackPerLevel = 2f;
    public float defensePerLevel = 1.5f;
    public float expToLevelUp = 100f;

    [Header("HUD Config")]
    public int fontSize = 14;
    public int barWidth = 160;
    public int barHeight = 6;

    public float Attack { get; private set; }
    public float Defense { get; private set; }
    public int Level { get; private set; } = 1;
    public float Exp { get; private set; }

    private Texture2D _bg, _barBg, _atkFill, _defFill, _expFill, _hpFill, _jetFill;
    private GUIStyle _labelStyle, _valueStyle, _timerStyle;
    private bool _stylesReady;

    private HealthController m_PlayerHealth;
    private float _lastTimeUpdate;
    private string _cachedTimeStr = "00:00";
    private Camera _mainCamCache;

    void Update()
    {
        // Billboard effect para el NameTag en red (Cacheando la camara para evitar Starvation)
        if (nameTagText != null)
        {
            if (_mainCamCache == null) _mainCamCache = Camera.main;
            if (_mainCamCache != null)
            {
                nameTagText.transform.rotation = Quaternion.LookRotation(nameTagText.transform.position - _mainCamCache.transform.position);
            }
        }
    }

    void Awake()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            InitializeStats();
        }
    }

    public override void OnNetworkSpawn()
    {
        m_PlayerHealth = GetComponent<HealthController>();
        if (IsOwner)
        {
            Instance = this;
            InitializeStats();
            playerName.Value = LocalUserConfig.UserName;
            playerColor.Value = LocalUserConfig.UserColor;
        }
        playerName.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
        playerColor.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
        UpdateVisuals();
    }

    void InitializeStats()
    {
        Attack = Random.Range(attackRange.x, attackRange.y);
        Defense = Random.Range(defenseRange.x, defenseRange.y);
    }

    public void UpdateVisuals()
    {
        ValidateVisualComponents();
        if (nameTagText != null) nameTagText.text = playerName.Value.ToString();
        if (colorRenderer != null) colorRenderer.material.color = playerColor.Value;
    }

    private void ValidateVisualComponents()
    {
        // Zero-Dependency Bootstrapping: Fallback para el Renderer
        if (colorRenderer == null)
        {
            colorRenderer = GetComponentInChildren<Renderer>();
            if (colorRenderer == null)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "Xenobot_IdentityMarker";
                marker.transform.SetParent(transform);
                marker.transform.localPosition = new Vector3(0, 2.2f, 0);
                marker.transform.localScale = Vector3.one * 0.2f;
                if (marker.TryGetComponent<Collider>(out var col)) DestroyImmediate(col);
                colorRenderer = marker.GetComponent<Renderer>();
            }
        }

        // Zero-Dependency Bootstrapping: Fallback para el TMP_Text
        if (nameTagText == null)
        {
            nameTagText = GetComponentInChildren<TMPro.TMP_Text>();
            if (nameTagText == null)
            {
                GameObject tagGO = new GameObject("Xenobot_NameTag");
                tagGO.transform.SetParent(transform);
                tagGO.transform.localPosition = new Vector3(0, 2.6f, 0);
                var tmp = tagGO.AddComponent<TMPro.TextMeshPro>();
                tmp.alignment = TMPro.TextAlignmentOptions.Center;
                tmp.fontSize = 4;
                tmp.rectTransform.sizeDelta = new Vector2(5, 1);
                nameTagText = tmp;
            }
        }
    }

    public void AddExp(float amount)
    {
        Exp += amount;
        while (Exp >= expToLevelUp) { Exp -= expToLevelUp; LevelUp(); }
    }

    void LevelUp()
    {
        Level++;
        Attack += attackPerLevel;
        Defense += defensePerLevel;
        expToLevelUp *= 1.2f;
    }

    void EnsureAssets()
    {
        if (_stylesReady) return;
        _bg = MakeTex(new Color(0f, 0f, 0f, 0.6f));
        _barBg = MakeTex(new Color(0.1f, 0.1f, 0.1f, 0.8f));
        _atkFill = MakeTex(new Color(1f, 0.4f, 0f, 1f));
        _defFill = MakeTex(new Color(0f, 0.6f, 1f, 1f));
        _expFill = MakeTex(new Color(1f, 0.9f, 0f, 1f));
        _hpFill = MakeTex(new Color(0.9f, 0.1f, 0.1f, 1f));
        _jetFill = MakeTex(new Color(0f, 0.9f, 0.9f, 1f));

        _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        _valueStyle = new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Normal };
        _timerStyle = new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleCenter, fontSize = fontSize + 2 };
        _timerStyle.normal.textColor = Color.cyan;
        _stylesReady = true;
    }

    Texture2D MakeTex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return; // Optimization: only run on repaint
        if (SceneManager.GetActiveScene().name != "BiomaScene") return;
        if (!IsOwner) return;

        EnsureAssets();

        // Timer Cache (once per 0.5s)
        if (Time.time - _lastTimeUpdate > 0.5f)
        {
            float t = Time.timeSinceLevelLoad;
            LevelsMenu.ultimoTiempoSession = t;
            LevelsMenu.ultimoNivelSession = SceneManager.GetActiveScene().name;
            _cachedTimeStr = LevelsMenu.FormatTime(t);
            _lastTimeUpdate = Time.time;
        }

        DrawTopRightHUD();
        if (m_PlayerHealth != null) DrawBottomLeftHUD();
    }

    private void DrawTopRightHUD()
    {
        int rowH = fontSize + barHeight + 2;
        int totalH = rowH * 4 + 6;
        float x = Screen.width - barWidth;
        GUI.DrawTexture(new Rect(x, 0, barWidth, totalH), _bg);
        float curY = 2;
        GUI.Label(new Rect(x, curY, barWidth, fontSize + 4), $"TIME {_cachedTimeStr}", _timerStyle);
        curY += fontSize + 6;
        DrawRow(x, ref curY, " ATK", Attack, 100f, _atkFill);
        DrawRow(x, ref curY, " DEF", Defense, 100f, _defFill);
        DrawRow(x, ref curY, $" LVL {Level}", Exp, expToLevelUp, _expFill);
    }

    private void DrawBottomLeftHUD()
    {
        bool hasJet = m_PlayerHealth.maxJetpack > 0;
        int rowH = fontSize + barHeight + 2;
        int totalH = rowH * (hasJet ? 2 : 1) + 4;
        float y = Screen.height - totalH;
        GUI.DrawTexture(new Rect(0, y, barWidth, totalH), _bg);
        float curY = y + 2;
        if (hasJet) DrawRow(0, ref curY, " JET", m_PlayerHealth.JetpackFuel, m_PlayerHealth.maxJetpack, _jetFill);
        DrawRow(0, ref curY, " HP", m_PlayerHealth.CurrentHP, m_PlayerHealth.maxHealth, _hpFill);
    }

    void DrawRow(float x, ref float y, string label, float val, float max, Texture2D fill)
    {
        GUI.Label(new Rect(x, y, barWidth - 2, fontSize + 2), label, _labelStyle);
        GUI.Label(new Rect(x, y, barWidth - 2, fontSize + 2), val.ToString("F0"), _valueStyle);
        y += fontSize + 2;
        GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), _barBg);
        GUI.DrawTexture(new Rect(x, y, barWidth * Mathf.Clamp01(val / (max > 0 ? max : 1f)), barHeight), fill);
        y += barHeight;
    }
}
