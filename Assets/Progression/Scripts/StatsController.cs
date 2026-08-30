using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Combating.Scripts;
using Unity.Collections;
using NGO.Networking;

/// <summary>
/// Unified controller for character progression, HUD and Identity (Name/Color).
/// Handles Level, Attack, Defense, EXP and per-player visual tags.
/// </summary>
public class StatsController : NetworkBehaviour
{
    public static StatsController Instance { get; private set; }

    [Header("Identity & Visuals")]
    public NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(new FixedString32Bytes(""), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<Color> playerColor = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [SerializeField] private TMPro.TMP_Text nameTagText;
    [SerializeField] private Renderer colorRenderer;

    [Header("Initial Ranges (Random at Start)")]
    public Vector2 attackRange = new Vector2(5f, 15f);
    public Vector2 defenseRange = new Vector2(3f, 10f);

    [Header("Base Growth per Level")]
    public float attackPerLevel = 2f;
    public float defensePerLevel = 1.5f;

    [Header("EXP for Level Up")]
    public float expToLevelUp = 100f;

    [Header("HUD Configuration")]
    public int fontSize = 14;
    public int barWidth = 180;
    public int barHeight = 8;
    public float maxAttackScale = 100f;
    public float maxDefenseScale = 100f;

    // Current Values
    public float Attack { get; private set; }
    public float Defense { get; private set; }
    public int Level { get; private set; } = 1;
    public float Exp { get; private set; }

    // HUD Assets
    private Texture2D _bg, _barBg, _atkFill, _defFill, _expFill, _hpFill, _jetFill;
    private GUIStyle _labelStyle, _valueStyle, _timerStyle;
    private bool _stylesReady;

    // Player Reference
    private HealthController m_PlayerHealth;

    void Awake()
    {
        // For local testing/offline
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

            // Set networked identity
            playerName.Value = LocalUserConfig.UserName;
            playerColor.Value = LocalUserConfig.UserColor;
        }

        // Subscriptions for everyone (Remote and Local)
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
        if (nameTagText != null) nameTagText.text = playerName.Value.ToString();
        if (colorRenderer != null) colorRenderer.material.color = playerColor.Value;
    }

    public void AddExp(float amount)
    {
        Exp += amount;
        while (Exp >= expToLevelUp)
        {
            Exp -= expToLevelUp;
            LevelUp();
        }
    }

    void LevelUp()
    {
        Level++;
        Attack += attackPerLevel;
        Defense += defensePerLevel;
        expToLevelUp *= 1.2f;
    }

    // --- HUD LOGIC (OnGUI) ---

    void EnsureAssets()
    {
        if (_stylesReady) return;

        _bg = MakeTex(new Color(0f, 0f, 0f, 0.7f));
        _barBg = MakeTex(new Color(0.15f, 0.15f, 0.15f, 0.9f));

        _atkFill = MakeTex(new Color(1f, 0.5f, 0.1f, 1f));
        _defFill = MakeTex(new Color(0.2f, 0.5f, 1f, 1f));
        _expFill = MakeTex(new Color(1f, 0.8f, 0f, 1f));
        _hpFill = MakeTex(new Color(0.85f, 0.1f, 0.1f, 1f));
        _jetFill = MakeTex(new Color(0.1f, 0.85f, 0.85f, 1f));

        _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        _labelStyle.normal.textColor = Color.white;
        _valueStyle = new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Normal };
        _valueStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        _timerStyle = new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleCenter, fontSize = fontSize + 2 };
        _timerStyle.normal.textColor = Color.cyan;

        _stylesReady = true;
    }

    Texture2D MakeTex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

    void OnGUI()
    {
        if (SceneManager.GetActiveScene().name != "BiomaScene") return;
        if (!IsOwner) return; // Only draw HUD for local player

        EnsureAssets();
        DrawTopRightHUD();
        if (m_PlayerHealth != null) DrawBottomLeftHUD();
    }

    private void DrawTopRightHUD()
    {
        int rows = 1 + 2 + 1;
        int totalHeight = (fontSize + barHeight + 2) * rows + 4;
        float x = Screen.width - barWidth;
        float y = 0;

        GUI.DrawTexture(new Rect(x, y, barWidth, totalHeight), _bg);
        float curY = y + 2;

        float time = Time.timeSinceLevelLoad;
        LevelsMenu.ultimoTiempoSession = time;
        LevelsMenu.ultimoNivelSession = SceneManager.GetActiveScene().name;
        GUI.Label(new Rect(x, curY, barWidth, fontSize + 4), $"TIME {LevelsMenu.FormatTime(time)}", _timerStyle);
        curY += fontSize + 6;

        DrawStatRow(x, ref curY, barWidth, " ATK", Attack, maxAttackScale, _atkFill);
        DrawStatRow(x, ref curY, barWidth, " DEF", Defense, maxDefenseScale, _defFill);
        DrawStatRow(x, ref curY, barWidth, $" LVL {Level}", Exp, expToLevelUp, _expFill);
    }

    private void DrawBottomLeftHUD()
    {
        bool hasJetpack = m_PlayerHealth.maxJetpack > 0;
        int rows = (hasJetpack ? 1 : 0) + 1;
        int totalHeight = (fontSize + barHeight + 2) * rows + 4;
        float x = 0; float y = Screen.height - totalHeight;

        GUI.DrawTexture(new Rect(x, y, barWidth, totalHeight), _bg);
        float curY = y + 2;

        if (hasJetpack)
            DrawStatRow(x, ref curY, barWidth, " JET", m_PlayerHealth.JetpackFuel, m_PlayerHealth.maxJetpack, _jetFill);

        DrawStatRow(x, ref curY, barWidth, " HP", m_PlayerHealth.CurrentHP, m_PlayerHealth.maxHealth, _hpFill);
    }

    void DrawStatRow(float x, ref float y, float w, string label, float val, float max, Texture2D fill)
    {
        GUI.Label(new Rect(x, y, w - 2, fontSize + 2), label, _labelStyle);
        GUI.Label(new Rect(x, y, w - 2, fontSize + 2), val.ToString("F0"), _valueStyle);
        y += fontSize + 2;
        GUI.DrawTexture(new Rect(x, y, w, barHeight), _barBg);
        GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(val / (max > 0 ? max : 1f)), barHeight), fill);
        y += barHeight;
    }
}
