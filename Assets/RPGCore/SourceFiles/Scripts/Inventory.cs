using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
    public int cornerRadius = 12;

    int CellSizeSel => Mathf.RoundToInt(cellSize * 1.25f);

    [Header("Drop")]
    public GameObject droppedItemPrefab;  // Prefab con Pickup (opcional)
    public float dropDistance = 2.5f;

    // Datos
    readonly Dictionary<string, (ItemDefinition def, int qty)> _bag = new();
    readonly List<string> _keys = new();
    readonly HashSet<string> _equipped = new();

    int _selectedIndex = -1;
    int _dropdownIndex = -1;
    bool _open;

    // Drag
    int _dragIndex = -1;
    Vector2 _dragPos;
    bool _dragging = false;
    bool _dragOutside = false;

    PlayerInput _playerInput;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        _playerInput = FindFirstObjectByType<PlayerInput>();
        if (_playerInput == null)
            Debug.LogWarning("[Inventory] No se encontro PlayerInput en la escena.");
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

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
            string sk = k + "_" + idx;
            Instance._bag[sk] = (def, 1);
            Instance._keys.Add(sk);
        }
        Debug.Log($"[Inventory] +1 '{def.itemId}' — {Instance._bag.Count} slot(s)");
    }

    void RemoveOne(string key)
    {
        if (!_bag.TryGetValue(key, out var slot)) return;
        if (slot.qty > 1)
        {
            _bag[key] = (slot.def, slot.qty - 1);
        }
        else
        {
            _bag.Remove(key);
            _keys.Remove(key);
            _equipped.Remove(key);
            if (_selectedIndex >= _keys.Count) _selectedIndex = _keys.Count - 1;
            _dropdownIndex = -1;
        }
    }

    void SpawnDropped(ItemDefinition def)
    {
        var playerGo = GameObject.FindGameObjectWithTag("Player");
        Vector3 pos = playerGo != null
            ? playerGo.transform.position + playerGo.transform.forward * dropDistance + Vector3.up * 0.5f
            : Vector3.zero;

        if (droppedItemPrefab != null)
        {
            var go = Instantiate(droppedItemPrefab, pos, Quaternion.identity);
            var p = go.GetComponent<Pickup>();
            if (p != null) p.item = def;
        }
        else
        {
            var go = new GameObject("Dropped_" + def.itemId);
            go.transform.position = pos;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true; col.radius = 0.5f;
            if (def.icon != null)
            {
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = def.icon;
                // Tamaño fijo en world-units sin importar resolución del sprite
                float ppu = def.icon.pixelsPerUnit > 0 ? def.icon.pixelsPerUnit : 100f;
                float maxSide = Mathf.Max(def.icon.rect.width, def.icon.rect.height) / ppu;
                float desiredSize = 0.6f; // metros en mundo; ajustar aquí si hace falta
                go.transform.localScale = Vector3.one * ((maxSide > 0f) ? desiredSize / maxSide : 1f);
            }
            var pickup = go.AddComponent<Pickup>();
            pickup.item = def;
        }
        Debug.Log($"[Inventory] Dropped '{def.itemId}' al mundo");
    }

    void SetOpen(bool open)
    {
        _open = open;
        if (_playerInput != null) _playerInput.enabled = !open;
        if (open)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _selectedIndex = -1;
            _dropdownIndex = -1;
            _dragging = false;
            _dragIndex = -1;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(KeyCode.Tab))
            SetOpen(!_open);
    }

    float PanelX() => (Screen.width - panelWidth) / 2f;
    float PanelY() => (Screen.height - panelHeight) / 2f;
    Rect PanelRect() => new Rect(PanelX(), PanelY(), panelWidth, panelHeight);

    Vector2 CellCenter(int i)
    {
        return new Vector2(
            PanelX() + padding + (i % columns) * (cellSize + padding) + cellSize * 0.5f,
            PanelY() + titleH + (i / columns) * (cellSize + padding) + cellSize * 0.5f);
    }

    Rect CellRect(int i) { var c = CellCenter(i); float h = cellSize * 0.5f; return new Rect(c.x - h, c.y - h, cellSize, cellSize); }
    Rect CellRectSel(int i) { var c = CellCenter(i); float h = CellSizeSel * 0.5f; return new Rect(c.x - h, c.y - h, CellSizeSel, CellSizeSel); }

    Rect DropdownRectFor(int i)
    {
        Rect cell = CellRectSel(i); int dw = 130, dh = 38;
        float dx = cell.x, dy = cell.yMax + 4;
        if (dx + dw > PanelX() + panelWidth - padding) dx = cell.xMax - dw;
        if (dy + dh > PanelY() + panelHeight - padding) dy = cell.y - dh - 4;
        return new Rect(dx, dy, dw, dh);
    }

    Texture2D _texNormal, _texSelected, _texDropdown, _texPanel, _texDropZone, _texGhost;

    Texture2D MakeRoundedTex(int s, int r, Color fill, Color border, int bw)
    {
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        Color clear = new Color(0, 0, 0, 0);
        Color[] px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float cx = Mathf.Clamp(x, r, s - 1 - r), cy = Mathf.Clamp(y, r, s - 1 - r);
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d > r + 1f) px[y * s + x] = clear;
                else if (d > r - 0.5f) px[y * s + x] = Color.Lerp(fill, clear, d - (r - 0.5f));
                else if (bw > 0 && d > r - bw) px[y * s + x] = border;
                else px[y * s + x] = fill;
            }
        tex.SetPixels(px); tex.Apply(); return tex;
    }

    void EnsureTextures()
    {
        if (_texNormal != null) return;
        int s = 64;
        _texNormal = MakeRoundedTex(s, cornerRadius, new Color(1f, 1f, 1f, 0.10f), Color.clear, 0);
        _texSelected = MakeRoundedTex(s, cornerRadius, new Color(0.08f, 0.08f, 0.08f, 0.97f), new Color(1f, 0.85f, 0f, 1f), 3);
        _texDropdown = MakeRoundedTex(s, 8, new Color(0.10f, 0.10f, 0.10f, 0.97f), new Color(1f, 0.85f, 0f, 0.6f), 1);
        _texPanel = MakeRoundedTex(s, 20, new Color(0f, 0f, 0f, 0.93f), Color.clear, 0);
        _texDropZone = MakeRoundedTex(s, 16, new Color(0.8f, 0.2f, 0.1f, 0.55f), new Color(1f, 0.4f, 0.1f, 0.9f), 3);
        _texGhost = MakeRoundedTex(s, cornerRadius, new Color(1f, 1f, 1f, 0.30f), Color.clear, 0);
    }

    GUIStyle _titleSty, _qtySty, _emptySty, _badgeSty, _ddNorm, _ddHov, _dropHintSty;
    bool _stylesReady;

    void EnsureStyles()
    {
        if (_stylesReady) return;
        _titleSty = Sty(28, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        _qtySty = Sty(qtyFontSize, FontStyle.Bold, TextAnchor.LowerRight, Color.white);
        _emptySty = Sty(18, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f));
        _badgeSty = Sty(20, FontStyle.Bold, TextAnchor.UpperRight, Color.yellow);
        _ddNorm = Sty(15, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        _ddHov = Sty(15, FontStyle.Bold, TextAnchor.MiddleCenter, Color.yellow);
        _dropHintSty = Sty(17, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.8f, 1f));
        _stylesReady = true;
    }

    static GUIStyle Sty(int sz, FontStyle fs, TextAnchor a, Color c)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = sz, fontStyle = fs, alignment = a };
        s.normal.textColor = c; return s;
    }

    void DrawCell(int i, bool selected, bool ghost)
    {
        string key = _keys[i];
        if (!_bag.TryGetValue(key, out var slot)) return;
        var def = slot.def;
        bool equipped = _equipped.Contains(key);
        Rect cell = selected ? CellRectSel(i) : CellRect(i);
        int size = (int)cell.width;

        GUI.color = Color.white;
        GUI.DrawTexture(cell, ghost ? _texGhost : (selected ? _texSelected : _texNormal));

        if (def.icon != null)
        {
            int m = 10, mb = def.isStackable ? qtyFontSize + 6 : m;
            GUI.color = ghost ? new Color(1, 1, 1, 0.4f) : Color.white;
            GUI.DrawTexture(new Rect(cell.x + m, cell.y + m, size - m * 2, size - m - mb),
                def.icon.texture, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }

        if (!ghost)
        {
            // Cantidad: esquina inferior derecha
            if (def.isStackable)
                GUI.Label(new Rect(cell.x + 2, cell.y + 2, size - 6, size - 6), "x" + slot.qty, _qtySty);
            // Badge equipado: esquina superior izquierda, no se pisa con qty
            if (equipped)
            {
                var badgeSty = new GUIStyle(_badgeSty);
                badgeSty.fontSize = Mathf.RoundToInt(size * 0.18f);
                badgeSty.alignment = TextAnchor.UpperLeft;
                GUI.Label(new Rect(cell.x + 6, cell.y + 4, size - 8, size - 8), "E", badgeSty);
            }
        }
    }

    void DrawDragGhost()
    {
        if (!_dragging || _dragIndex < 0 || _dragIndex >= _keys.Count) return;
        string key = _keys[_dragIndex];
        if (!_bag.TryGetValue(key, out var slot)) return;

        int sz = cellSize;
        Rect r = new Rect(_dragPos.x - sz * 0.5f, _dragPos.y - sz * 0.5f, sz, sz);
        GUI.color = Color.white;
        GUI.DrawTexture(r, _texGhost);
        if (slot.def.icon != null)
        {
            int m = 10;
            GUI.color = new Color(1, 1, 1, 0.8f);
            GUI.DrawTexture(new Rect(r.x + m, r.y + m, sz - m * 2, sz - m * 2), slot.def.icon.texture, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }
    }

    void OnGUI()
    {
        if (!_open) return;
        EnsureStyles();
        EnsureTextures();

        Event e = Event.current;
        Rect panel = PanelRect();
        float x0 = PanelX(), y0 = PanelY();
        Vector2 mp = e.mousePosition;

        if (_dragging) _dragPos = mp;
        _dragOutside = _dragging && !panel.Contains(mp);

        // 1 — Panel
        GUI.color = Color.white;
        GUI.DrawTexture(panel, _texPanel);
        GUI.Label(new Rect(x0, y0 + 8, panelWidth, titleH), "INVENTARIO", _titleSty);

        // 2 — Celdas normales
        for (int i = 0; i < _keys.Count; i++)
        {
            if (i == _selectedIndex && !_dragging) continue;
            DrawCell(i, false, _dragging && i == _dragIndex);
        }

        if (_bag.Count == 0)
            GUI.Label(new Rect(x0, y0 + titleH, panelWidth, panelHeight - titleH), "Inventario vacio", _emptySty);

        // 3 — Celda seleccionada encima
        if (!_dragging && _selectedIndex >= 0 && _selectedIndex < _keys.Count)
            DrawCell(_selectedIndex, true, false);

        // 4 — Dropdown
        if (!_dragging && _dropdownIndex >= 0 && _dropdownIndex < _keys.Count)
        {
            string key = _keys[_dropdownIndex];
            if (_bag.TryGetValue(key, out var slot) && slot.def.type == ItemType.Equipment)
            {
                string label = _equipped.Contains(key) ? "Desequipar" : "Equipar";
                Rect dd = DropdownRectFor(_dropdownIndex);
                GUI.color = Color.white;
                GUI.DrawTexture(dd, _texDropdown);
                GUI.Label(dd, label, dd.Contains(mp) ? _ddHov : _ddNorm);
            }
        }

        // 5 — Zona de drop
        if (_dragOutside)
        {
            Rect dz = new Rect(x0, y0 + panelHeight + 10, panelWidth, 60);
            GUI.color = Color.white;
            GUI.DrawTexture(dz, _texDropZone);
            GUI.Label(dz, "Soltar aqui para tirar al suelo", _dropHintSty);
        }

        // 6 — Ghost flotante
        DrawDragGhost();

        // 7 — Mouse Down
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            bool handled = false;

            // Celda seleccionada (hitbox grande) — iniciar drag
            if (_selectedIndex >= 0 && _selectedIndex < _keys.Count
                && CellRectSel(_selectedIndex).Contains(mp))
            {
                _dragIndex = _selectedIndex;
                _dragging = true; _dragPos = mp; _dropdownIndex = -1;
                handled = true; e.Use();
            }

            // Cualquier celda normal
            if (!handled)
            {
                for (int i = 0; i < _keys.Count; i++)
                {
                    if (CellRect(i).Contains(mp))
                    {
                        _selectedIndex = i; _dragIndex = i;
                        _dragging = true; _dragPos = mp; _dropdownIndex = -1;
                        handled = true; e.Use(); break;
                    }
                }
            }

            // Dropdown
            if (!handled && _dropdownIndex >= 0 && _dropdownIndex < _keys.Count)
            {
                Rect dd = DropdownRectFor(_dropdownIndex);
                if (dd.Contains(mp))
                {
                    string key = _keys[_dropdownIndex];
                    if (_bag.TryGetValue(key, out var slot) && slot.def.type == ItemType.Equipment)
                    {
                        if (_equipped.Contains(key)) _equipped.Remove(key);
                        else _equipped.Add(key);
                        Debug.Log($"[Inventory] '{slot.def.displayName}' EQUIPADO={_equipped.Contains(key)}");
                    }
                    _dropdownIndex = -1; handled = true; e.Use();
                }
                else { _dropdownIndex = -1; }
            }
        }

        // 8 — Mouse Up
        if (e.type == EventType.MouseUp && e.button == 0 && _dragging)
        {
            if (_dragOutside)
            {
                // Tirar al suelo
                string key = _keys[_dragIndex];
                if (_bag.TryGetValue(key, out var slot))
                {
                    SpawnDropped(slot.def);
                    RemoveOne(key);
                    _selectedIndex = -1;
                }
            }
            else
            {
                // Soltar dentro: abrir dropdown si es Equipment
                _selectedIndex = _dragIndex;
                string key = _keys[_dragIndex];
                if (_bag.TryGetValue(key, out var slot) && slot.def.type == ItemType.Equipment)
                    _dropdownIndex = _dragIndex;
            }
            _dragging = false; _dragIndex = -1;
            e.Use();
        }

        // 9 — Teclado
        if (e.type == EventType.KeyDown)
        {
            int count = _keys.Count;
            if (e.keyCode == KeyCode.Escape)
            {
                if (_dragging) { _dragging = false; _dragIndex = -1; }
                else if (_dropdownIndex >= 0) { _dropdownIndex = -1; }
                else if (_selectedIndex >= 0) { _selectedIndex = -1; _dropdownIndex = -1; }
                else { SetOpen(false); }
                e.Use();
            }
            else if (!_dragging)
            {
                if (e.keyCode == KeyCode.RightArrow && count > 0) { _selectedIndex = (_selectedIndex < 0) ? 0 : Mathf.Min(_selectedIndex + 1, count - 1); _dropdownIndex = -1; e.Use(); }
                else if (e.keyCode == KeyCode.LeftArrow && count > 0) { _selectedIndex = (_selectedIndex < 0) ? 0 : Mathf.Max(_selectedIndex - 1, 0); _dropdownIndex = -1; e.Use(); }
                else if (e.keyCode == KeyCode.DownArrow && count > 0) { _selectedIndex = (_selectedIndex < 0) ? 0 : Mathf.Min(_selectedIndex + columns, count - 1); _dropdownIndex = -1; e.Use(); }
                else if (e.keyCode == KeyCode.UpArrow && count > 0) { _selectedIndex = (_selectedIndex < 0) ? 0 : Mathf.Max(_selectedIndex - columns, 0); _dropdownIndex = -1; e.Use(); }
                else if (e.keyCode == KeyCode.Space && _selectedIndex >= 0 && _selectedIndex < count)
                {
                    string k = _keys[_selectedIndex];
                    if (_bag.TryGetValue(k, out var s2) && s2.def.type == ItemType.Equipment)
                        _dropdownIndex = (_dropdownIndex == _selectedIndex) ? -1 : _selectedIndex;
                    e.Use();
                }
            }
        }
    }
}