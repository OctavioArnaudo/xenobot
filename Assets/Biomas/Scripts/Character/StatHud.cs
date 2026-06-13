using UnityEngine;

/// <summary>
/// Panel HUD minimalista con nivel, ATK, DEF y barra de EXP.
/// Mismo estilo visual que el inventario y el reloj.
/// Attach a cualquier GameObject.
/// </summary>
public class StatsHUD : MonoBehaviour
{
    [Header("Panel")]
    public int width = 160;
    public int height = 90;
    public int margin = 16;
    public int fontSize = 15;

    Texture2D _bg, _barBg, _barFill;
    GUIStyle _style;
    bool _ready;

    void EnsureAssets()
    {
        if (_ready) return;

        _bg = MakeTex(new Color(0f, 0f, 0f, 0.78f));
        _barBg = MakeTex(new Color(0.2f, 0.05f, 0.05f, 0.9f));
        _barFill = MakeTex(new Color(1f, 0.75f, 0.1f, 1f));

        _style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
        };
        _style.normal.textColor = new Color(1f, 0.85f, 0.7f);
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
        var s = CharacterStats.Instance;
        if (s == null) return;
        EnsureAssets();

        // Posición: esquina superior derecha debajo del reloj (BiomaHUD tiene ~42px + 16 margen)
        float x = Screen.width - width - margin;
        float y = margin + 42 + 8;

        Rect panel = new Rect(x, y, width, height);
        GUI.DrawTexture(panel, _bg);

        float pad = 8f;
        GUI.Label(new Rect(x + pad, y + 4, width, 20), $"Nivel  {s.Level}", _style);
        GUI.Label(new Rect(x + pad, y + 22, width, 20), $"ATK  {s.Attack:F1}", _style);
        GUI.Label(new Rect(x + pad, y + 40, width, 20), $"DEF  {s.Defense:F1}", _style);

        // Barra EXP
        float expMax = s.expToLevelUp > 0 ? s.expToLevelUp : 1f; // acceso público
        // Necesitamos expToLevelUp público — ver CharacterStats
        Rect bgBar = new Rect(x + pad, y + 64, width - pad * 2, 12);
        Rect fillBar = new Rect(bgBar.x, bgBar.y, bgBar.width * Mathf.Clamp01(s.Exp / expMax), 12);
        GUI.DrawTexture(bgBar, _barBg);
        GUI.DrawTexture(fillBar, _barFill);
    }
}