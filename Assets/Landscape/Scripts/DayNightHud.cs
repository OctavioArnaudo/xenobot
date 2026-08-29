using UnityEngine;

/// <summary>
/// Draws a minimal clock panel in the top-right corner.
/// Same visual language as Inventory (dark panel, white text).
/// Requires DayNightCycle in the scene.
/// </summary>
public class DayNightHUD : MonoBehaviour
{
    [Header("Panel")]
    public int panelWidth = 140;
    public int panelHeight = 44;
    public int margin = 16;
    public int fontSize = 22;
    public int cornerRadius = 12;

    Texture2D _bg;
    GUIStyle _style;
    bool _ready;

    void EnsureAssets()
    {
        if (_ready) return;

        // Rounded dark panel — same technique as Inventory
        int s = 64;
        _bg = new Texture2D(s, s, TextureFormat.RGBA32, false);
        _bg.filterMode = FilterMode.Bilinear;
        Color fill = new Color(0f, 0f, 0f, 0.82f);
        Color clear = new Color(0, 0, 0, 0);
        Color[] px = new Color[s * s];
        int r = cornerRadius;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float cx = Mathf.Clamp(x, r, s - 1 - r);
                float cy = Mathf.Clamp(y, r, s - 1 - r);
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                px[y * s + x] = d > r + 1f ? clear :
                                d > r - 0.5f ? Color.Lerp(fill, clear, d - (r - 0.5f)) : fill;
            }
        _bg.SetPixels(px);
        _bg.Apply();

        _style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _style.normal.textColor = Color.white;

        _ready = true;
    }

    void OnGUI()
    {
        if (DayNightCycle.Instance == null) return;
        EnsureAssets();

        float hour = DayNightCycle.Instance.CurrentHour;
        int h = (int)hour % 24;
        int m = (int)((hour - (int)hour) * 60f);
        bool isDay = h >= 6 && h < 20;

        string icon = isDay ? "☀" : "☾";
        string label = $"{icon}  {h:D2}:{m:D2}";

        float x = Screen.width - panelWidth - margin;
        float y = margin;
        Rect panel = new Rect(x, y, panelWidth, panelHeight);

        GUI.color = Color.white;
        GUI.DrawTexture(panel, _bg);
        GUI.Label(panel, label, _style);
    }
}