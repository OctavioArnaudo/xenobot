using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    public static Inventory Instance { get; private set; }

    public KeyCode toggleKey = KeyCode.I;

    [Header("Panel")]
    public int panelWidth = 800;
    public int panelHeight = 600;

    [Header("Celdas")]
    public int columns = 6;
    public int cellSize = 110;
    public int padding = 14;
    public int titleH = 60;
    public int qtyFontSize = 22;

    readonly Dictionary<string, (ItemDefinition def, int qty)> _bag = new();
    bool _open;

    // ── Singleton ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── Agregar ítem ──────────────────────────────────────────────────────────
    public static void Add(ItemDefinition def)
    {
        if (def == null || Instance == null) { Debug.LogWarning("[Inventory] Add failed — def or Instance is null"); return; }

        string k = def.itemId.ToLowerInvariant();

        if (def.isStackable)
        {
            Instance._bag[k] = Instance._bag.TryGetValue(k, out var v)
                ? (v.def, Mathf.Min(v.qty + 1, def.maxStack))
                : (def, 1);
        }
        else
        {
            int idx = 0;
            while (Instance._bag.ContainsKey(k + "_" + idx)) idx++;
            Instance._bag[k + "_" + idx] = (def, 1);
        }

        Debug.Log($"[Inventory] +1 '{def.itemId}' — bag has {Instance._bag.Count} slot(s)");
    }

    // ── Input ─────────────────────────────────────────────────────────────────
    void Update()
    {
        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(KeyCode.Tab))
            _open = !_open;
    }

    // ── Estilos (se crean una sola vez) ───────────────────────────────────────
    GUIStyle _titleStyle;
    GUIStyle _qtyStyle;
    GUIStyle _emptyStyle;
    bool _stylesReady;

    void EnsureStyles()
    {
        if (_stylesReady) return;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 28,
            fontStyle = FontStyle.Bold
        };
        _titleStyle.normal.textColor = Color.white;

        _qtyStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerRight,
            fontSize = qtyFontSize,
            fontStyle = FontStyle.Bold
        };
        _qtyStyle.normal.textColor = Color.white;

        _emptyStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18
        };
        _emptyStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);

        _stylesReady = true;
    }

    // ── Render ────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!_open) return;
        EnsureStyles();

        float x0 = (Screen.width - panelWidth) / 2f;
        float y0 = (Screen.height - panelHeight) / 2f;

        // Fondo del panel
        GUI.color = new Color(0f, 0f, 0f, 0.93f);
        GUI.DrawTexture(new Rect(x0, y0, panelWidth, panelHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Título
        GUI.Label(new Rect(x0, y0 + 8, panelWidth, titleH), "INVENTARIO", _titleStyle);

        // Celdas
        int i = 0;
        foreach (var kv in _bag)
        {
            var def = kv.Value.def;
            int qty = kv.Value.qty;

            int col = i % columns;
            int row = i / columns;

            float cx = x0 + padding + col * (cellSize + padding);
            float cy = y0 + titleH + row * (cellSize + padding);

            // Fondo celda
            GUI.color = new Color(1f, 1f, 1f, 0.10f);
            GUI.DrawTexture(new Rect(cx, cy, cellSize, cellSize), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Ícono (deja margen abajo para el contador)
            if (def.icon != null)
            {
                int iconMargin = 8;
                int iconBottom = def.isStackable ? qtyFontSize + 4 : iconMargin;
                GUI.DrawTexture(
                    new Rect(cx + iconMargin, cy + iconMargin,
                             cellSize - iconMargin * 2,
                             cellSize - iconMargin - iconBottom),
                    def.icon.texture, ScaleMode.ScaleToFit);
            }

            // Cantidad
            if (def.isStackable)
                GUI.Label(new Rect(cx + 2, cy + 2, cellSize - 6, cellSize - 6), "x" + qty, _qtyStyle);

            i++;
        }

        if (_bag.Count == 0)
            GUI.Label(new Rect(x0, y0 + titleH, panelWidth, panelHeight - titleH), "Inventario vacío", _emptyStyle);
    }
}