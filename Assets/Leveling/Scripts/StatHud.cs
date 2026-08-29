using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Panel HUD responsivo con sliders para Ataque, Defensa y Nivel.
/// Incluye un timer de nivel en la parte superior.
/// </summary>
public class StatsHUD : MonoBehaviour
{
    [Header("Configuración")]
    public int margin = 0;
    public int fontSize = 14;
    public int barWidth = 180;
    public int barHeight = 8;

    [Header("Escalas Máximas (para sliders)")]
    public float maxAttackScale = 100f;
    public float maxDefenseScale = 100f;

    Texture2D _bg, _barBg;
    Texture2D _atkFill, _defFill, _expFill, _timerFill;
    GUIStyle _labelStyle, _valueStyle, _timerStyle;
    bool _ready;

    void EnsureAssets()
    {
        if (_ready) return;

        _bg = MakeTex(new Color(0f, 0f, 0f, 0.7f));
        _barBg = MakeTex(new Color(0.15f, 0.15f, 0.15f, 0.9f));

        _atkFill = MakeTex(new Color(1f, 0.5f, 0.1f, 1f));   // Naranja Ataque
        _defFill = MakeTex(new Color(0.2f, 0.5f, 1f, 1f));   // Azul Defensa
        _expFill = MakeTex(new Color(1f, 0.8f, 0f, 1f));     // Amarillo EXP
        _timerFill = MakeTex(new Color(0.5f, 0.5f, 0.5f, 1f)); // Gris Timer

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(0, 0, 0, 0)
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

        _ready = true;
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

        var stats = CharacterStats.Instance;
        if (stats == null) return;

        EnsureAssets();

        // 1. Calcular dimensiones del panel (Timer + Atk + Def + Exp)
        int rows = 1 + 2 + 1; // Timer + Atk + Def + Exp
        int rowH = fontSize + barHeight + 2;
        int totalWidth = barWidth;
        int totalHeight = rowH * rows + 4;

        float x = Screen.width - totalWidth;
        float y = 0;

        // Fondo
        GUI.DrawTexture(new Rect(x, y, totalWidth, totalHeight), _bg);

        float curY = y + 2;
        float innerX = x;
        float innerW = barWidth;

        // --- TIMER ---
        float time = Time.timeSinceLevelLoad;
        string timerStr = string.Format("{0:00}:{1:00}", Mathf.FloorToInt(time / 60), Mathf.FloorToInt(time % 60));
        GUI.Label(new Rect(innerX, curY, innerW, fontSize + 4), $"TIME {timerStr}", _timerStyle);
        curY += fontSize + 6;

        // --- ATAQUE ---
        DrawStatRow(innerX, ref curY, innerW, " ATK", stats.Attack, maxAttackScale, _atkFill);

        // --- DEFENSA ---
        DrawStatRow(innerX, ref curY, innerW, " DEF", stats.Defense, maxDefenseScale, _defFill);

        // --- EXP ---
        float expMax = stats.expToLevelUp > 0 ? stats.expToLevelUp : 1f;
        DrawStatRow(innerX, ref curY, innerW, $" LVL {stats.Level}", stats.Exp, expMax, _expFill);
    }

    void DrawStatRow(float x, ref float y, float w, string label, float val, float max, Texture2D fill)
    {
        // Texto
        GUI.Label(new Rect(x, y, w - 2, fontSize + 2), label, _labelStyle);
        GUI.Label(new Rect(x, y, w - 2, fontSize + 2), val.ToString("F0"), _valueStyle);
        y += fontSize + 2;

        // Slider (Barra)
        Rect bgRect = new Rect(x, y, w, barHeight);
        GUI.DrawTexture(bgRect, _barBg);

        float ratio = Mathf.Clamp01(val / max);
        GUI.DrawTexture(new Rect(x, y, w * ratio, barHeight), fill);

        y += barHeight;
    }
}
