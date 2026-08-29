using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

/// <summary>
/// Unified controller for character progression and HUD display.
/// Handles Level, Attack, Defense, EXP and OnGUI visualization.
/// </summary>
public class StatsController : NetworkBehaviour
{
    public static StatsController Instance { get; private set; }

    [Header("Initial Ranges (Random at Start)")]
    public Vector2 attackRange = new Vector2(5f, 15f);
    public Vector2 defenseRange = new Vector2(3f, 10f);

    [Header("Base Growth per Level")]
    public float attackPerLevel = 2f;
    public float defensePerLevel = 1.5f;

    [Header("EXP for Level Up")]
    public float expToLevelUp = 100f; // Increases 20% per level

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
    private Texture2D _bg, _barBg, _atkFill, _defFill, _expFill;
    private GUIStyle _labelStyle, _valueStyle, _timerStyle;
    private bool _stylesReady;

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
        if (IsOwner)
        {
            Instance = this;
            InitializeStats();
        }
        else
        {
            this.enabled = false;
        }
    }

    void InitializeStats()
    {
        Attack = Random.Range(attackRange.x, attackRange.y);
        Defense = Random.Range(defenseRange.x, defenseRange.y);
        Debug.Log($"[Stats] Initialized: Level {Level} | ATK {Attack:F1} | DEF {Defense:F1}");
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
        Debug.Log($"[Stats] Level UP: {Level}! | ATK {Attack:F1} | DEF {Defense:F1}");
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

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        _labelStyle.normal.textColor = Color.white;

        _valueStyle = new GUIStyle(_labelStyle)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Normal
        };
        _valueStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

        _timerStyle = new GUIStyle(_labelStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize + 2
        };
        _timerStyle.normal.textColor = Color.cyan;

        _stylesReady = true;
    }

    Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void OnGUI()
    {
        if (SceneManager.GetActiveScene().name != "BiomaScene") return;
        if (Instance == null) return;

        EnsureAssets();

        int rows = 1 + 2 + 1; // Timer + Atk + Def + Exp
        int rowH = fontSize + barHeight + 2;
        int totalWidth = barWidth;
        int totalHeight = rowH * rows + 4;

        float x = Screen.width - totalWidth;
        float y = 0;

        GUI.DrawTexture(new Rect(x, y, totalWidth, totalHeight), _bg);

        float curY = y + 2;
        float innerX = x;
        float innerW = barWidth;

        // Timer
        float time = Time.timeSinceLevelLoad;
        LevelsMenu.ultimoTiempoSession = time;
        LevelsMenu.ultimoNivelSession = SceneManager.GetActiveScene().name;
        string timerStr = LevelsMenu.FormatTime(time);
        GUI.Label(new Rect(innerX, curY, innerW, fontSize + 4), $"TIME {timerStr}", _timerStyle);
        curY += fontSize + 6;

        // Attack
        DrawStatRow(innerX, ref curY, innerW, " ATK", Attack, maxAttackScale, _atkFill);
        // Defense
        DrawStatRow(innerX, ref curY, innerW, " DEF", Defense, maxDefenseScale, _defFill);
        // Level/Exp
        float expMax = expToLevelUp > 0 ? expToLevelUp : 1f;
        DrawStatRow(innerX, ref curY, innerW, $" LVL {Level}", Exp, expMax, _expFill);
    }

    void DrawStatRow(float x, ref float y, float w, string label, float val, float max, Texture2D fill)
    {
        GUI.Label(new Rect(x, y, w - 2, fontSize + 2), label, _labelStyle);
        GUI.Label(new Rect(x, y, w - 2, fontSize + 2), val.ToString("F0"), _valueStyle);
        y += fontSize + 2;

        Rect bgRect = new Rect(x, y, w, barHeight);
        GUI.DrawTexture(bgRect, _barBg);
        float ratio = Mathf.Clamp01(val / max);
        GUI.DrawTexture(new Rect(x, y, w * ratio, barHeight), fill);
        y += barHeight;
    }
}
