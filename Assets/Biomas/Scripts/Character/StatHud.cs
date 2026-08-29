using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.FPS.Game; // Para acceder al script Health

/// <summary>
/// Panel HUD responsivo con sliders para Vida, Ataque y Defensa.
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
    Texture2D _hpFill, _atkFill, _defFill, _expFill;
    GUIStyle _labelStyle, _valueStyle;
    bool _ready;

    void EnsureAssets()
    {
        if (_ready) return;

        _bg = MakeTex(new Color(0f, 0f, 0f, 0.7f));
        _barBg = MakeTex(new Color(0.15f, 0.15f, 0.15f, 0.9f));

        _hpFill = MakeTex(new Color(0.8f, 0.2f, 0.2f, 1f));   // Rojo Vida
        _atkFill = MakeTex(new Color(1f, 0.5f, 0.1f, 1f));   // Naranja Ataque
        _defFill = MakeTex(new Color(0.2f, 0.5f, 1f, 1f));   // Azul Defensa
        _expFill = MakeTex(new Color(1f, 0.8f, 0f, 1f));     // Amarillo EXP

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

        // Intentar obtener el componente Health del mismo objeto que CharacterStats
        Health health = stats.GetComponent<Health>();

        EnsureAssets();

        // 1. Calcular dimensiones del panel
        int rows = (health != null ? 1 : 0) + 2 + 1; // Health + Atk + Def + Exp
        int rowH = fontSize + barHeight + 2;
        int totalWidth = barWidth;
        int totalHeight = rowH * rows;

        float x = Screen.width - totalWidth;
        float y = 0;

        // Fondo
        GUI.DrawTexture(new Rect(x, y, totalWidth, totalHeight), _bg);

        float curY = y;
        float innerX = x;
        float innerW = barWidth;

        // --- VIDA ---
        if (health != null)
        {
            DrawStatRow(innerX, ref curY, innerW, " HP", health.CurrentHealth, health.MaxHealth, _hpFill);
        }

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
