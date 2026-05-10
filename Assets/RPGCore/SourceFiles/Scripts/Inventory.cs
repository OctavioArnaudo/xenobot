using System.Collections.Generic;
using UnityEngine;
// Compatible con New Input System — todo el input del inventario va por Event.current en OnGUI

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

    // ── Datos ─────────────────────────────────────────────────────────────────
    readonly Dictionary<string, (ItemDefinition def, int qty)> _bag = new();
    readonly List<string> _keys = new();
    readonly HashSet<string> _equipped = new();

    int _selectedIndex = -1;
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
        if (def == null || Instance == null) { Debug.LogWarning("[Inventory] Add failed"); return; }

        string k = def.itemId.ToLowerInvariant();

        if (def.isStackable)
        {
            if (!Instance._bag.ContainsKey(k)) Instance._keys.Add(k);
            Instance._bag[k] = Instance._bag.TryGetValue(k, out var v)
                ? (v.def, Mathf.Min(v.qty + 1, def.maxStack))
                : (def, 1);
        }
        else
        {
            int idx = 0;
            while (Instance._bag.ContainsKey(k + "_" + idx)) idx++;
            string slotKey = k + "_" + idx;
            Instance._bag[slotKey] = (def, 1);
            Instance._keys.Add(slotKey);
        }

        Debug.Log($"[Inventory] +1 '{def.itemId}' — {Instance._bag.Count} slot(s)");
    }

    // ── Update: solo toggle (KeyCode legacy funciona siempre) ─────────────────
    void Update()
    {
        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(KeyCode.Tab))
        {
            _open = !_open;
            if (!_open) _selectedIndex = -1;
        }
    }

    // ── Estilos ───────────────────────────────────────────────────────────────
    GUIStyle _titleStyle, _qtyStyle, _emptyStyle, _actionStyle, _equipBadgeStyle;
    bool _stylesReady;

    void EnsureStyles()
    {
        if (_stylesReady) return;
        _titleStyle = new GUIStyle(GUI.skin.label)
        { alignment = TextAnchor.MiddleCenter, fontSize = 28, fontStyle = FontStyle.Bold };
        _titleStyle.normal.textColor = Color.white;

        _qtyStyle = new GUIStyle(GUI.skin.label)
        { alignment = TextAnchor.LowerRight, fontSize = qtyFontSize, fontStyle = FontStyle.Bold };
        _qtyStyle.normal.textColor = Color.white;

        _emptyStyle = new GUIStyle(GUI.skin.label)
        { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
        _emptyStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);

        _actionStyle = new GUIStyle(GUI.skin.label)
        { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
        _actionStyle.normal.textColor = Color.yellow;

        _equipBadgeStyle = new GUIStyle(GUI.skin.label)
        { alignment = TextAnchor.UpperRight, fontSize = 20, fontStyle = FontStyle.Bold };
        _equipBadgeStyle.normal.textColor = Color.yellow;

        _stylesReady = true;
    }

    // ── OnGUI: render + todo el input del inventario ──────────────────────────
    void OnGUI()
    {
        if (!_open) return;
        EnsureStyles();

        // ── Capturar input de teclado con Event.current ───────────────────────
        // Esto funciona con New Input System porque OnGUI tiene su propio event loop
        Event e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            int count = _keys.Count;

            if (e.keyCode == KeyCode.RightArrow && count > 0)
            {
                _selectedIndex = (_selectedIndex < 0) ? 0 : Mathf.Min(_selectedIndex + 1, count - 1);
                e.Use();
            }
            else if (e.keyCode == KeyCode.LeftArrow && count > 0)
            {
                _selectedIndex = (_selectedIndex < 0) ? 0 : Mathf.Max(_selectedIndex - 1, 0);
                e.Use();
            }
            else if (e.keyCode == KeyCode.DownArrow && count > 0)
            {
                _selectedIndex = (_selectedIndex < 0) ? 0 : Mathf.Min(_selectedIndex + columns, count - 1);
                e.Use();
            }
            else if (e.keyCode == KeyCode.UpArrow && count > 0)
            {
                _selectedIndex = (_selectedIndex < 0) ? 0 : Mathf.Max(_selectedIndex - columns, 0);
                e.Use();
            }
            else if (e.keyCode == KeyCode.Space && _selectedIndex >= 0 && _selectedIndex < count)
            {
                string k = _keys[_selectedIndex];
                if (_bag.TryGetValue(k, out var slot) && slot.def.type == ItemType.Equipment)
                {
                    if (_equipped.Contains(k)) _equipped.Remove(k);
                    else _equipped.Add(k);
                    Debug.Log($"[Inventory] '{slot.def.displayName}' → {(_equipped.Contains(k) ? "EQUIPADO" : "DESEQUIPADO")}");
                }
                e.Use();
            }
        }

        // ── Render ────────────────────────────────────────────────────────────
        float x0 = (Screen.width - panelWidth) / 2f;
        float y0 = (Screen.height - panelHeight) / 2f;

        // Fondo
        GUI.color = new Color(0f, 0f, 0f, 0.93f);
        GUI.DrawTexture(new Rect(x0, y0, panelWidth, panelHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Título
        GUI.Label(new Rect(x0, y0 + 8, panelWidth, titleH), "INVENTARIO", _titleStyle);

        // Celdas
        for (int i = 0; i < _keys.Count; i++)
        {
            string key = _keys[i];
            if (!_bag.TryGetValue(key, out var slot)) continue;

            var def = slot.def;
            int qty = slot.qty;
            bool selected = (i == _selectedIndex);
            bool equipped = _equipped.Contains(key);

            int col = i % columns;
            int row = i / columns;

            float cx = x0 + padding + col * (cellSize + padding);
            float cy = y0 + titleH + row * (cellSize + padding);
            Rect cellRect = new Rect(cx, cy, cellSize, cellSize);

            // Fondo celda
            GUI.color = selected ? new Color(1f, 0.85f, 0f, 0.25f) : new Color(1f, 1f, 1f, 0.10f);
            GUI.DrawTexture(cellRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Borde si seleccionada
            if (selected) DrawBorder(cellRect, new Color(1f, 0.85f, 0f, 1f), 3);

            // Ícono
            if (def.icon != null)
            {
                int m = 8;
                int mb = def.isStackable ? qtyFontSize + 4 : m;
                GUI.DrawTexture(new Rect(cx + m, cy + m, cellSize - m * 2, cellSize - m - mb),
                    def.icon.texture, ScaleMode.ScaleToFit);
            }

            // Cantidad
            if (def.isStackable)
                GUI.Label(new Rect(cx + 2, cy + 2, cellSize - 6, cellSize - 6), "x" + qty, _qtyStyle);

            // Badge "E" equipado
            if (equipped)
                GUI.Label(new Rect(cx + 2, cy + 2, cellSize - 6, cellSize - 6), "E", _equipBadgeStyle);

            // Clic para seleccionar
            if (e.type == EventType.MouseDown && cellRect.Contains(e.mousePosition))
            {
                _selectedIndex = i;
                e.Use();
            }
        }

        if (_bag.Count == 0)
            GUI.Label(new Rect(x0, y0 + titleH, panelWidth, panelHeight - titleH), "Inventario vacío", _emptyStyle);

        // Hint inferior
        if (_selectedIndex >= 0 && _selectedIndex < _keys.Count)
        {
            string k = _keys[_selectedIndex];
            if (_bag.TryGetValue(k, out var slot) && slot.def.type == ItemType.Equipment)
            {
                bool eq = _equipped.Contains(k);
                string hint = eq ? "[ESPACIO] Desequipar" : "[ESPACIO] Equipar";
                GUI.Label(new Rect(x0, y0 + panelHeight - 36, panelWidth, 30), hint, _actionStyle);
            }
        }
    }

    void DrawBorder(Rect r, Color c, int t)
    {
        GUI.color = c;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}