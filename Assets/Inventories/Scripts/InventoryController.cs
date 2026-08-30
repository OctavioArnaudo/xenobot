using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class InventoryController : NetworkBehaviour
{
    public static InventoryController Instance { get; private set; }

    private static Dictionary<string, (ItemData def, int qty)> s_PersistentBag = new();
    private static List<string> s_PersistentKeys = new();
    private static HashSet<string> s_PersistentEquipped = new();

    [Header("Panel Settings")]
    public int panelWidth = 800;
    public int panelHeight = 600;
    public int columns = 6;
    public int cellSize = 110;
    public int padding = 14;
    public int titleH = 60;
    public int qtyFontSize = 22;
    public int cornerRadius = 12;

    [Header("Drop Settings")]
    public float dropDistance = 2.5f;
    public float droppedWorldSize = 0.4f;

    private Dictionary<string, (ItemData def, int qty)> _bag => s_PersistentBag;
    private List<string> _keys => s_PersistentKeys;
    private HashSet<string> _equipped => s_PersistentEquipped;

    private static int s_CollectiblesRemaining = 0;
    private static bool s_CountDirty = true;
    private float _countUpdateTimer = 0f;

    int _selectedIndex = -1;
    int _dropdownIndex = -1;
    bool _open;
    int _dragIndex = -1;
    Vector2 _dragPos;
    bool _dragging = false;
    bool _dragOutside = false;

    PlayerInput _playerInput;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            Instance = this;
            _playerInput = GetComponent<PlayerInput>();
        }
        else
        {
            this.enabled = false;
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this) Instance = null;
    }

    public static void Add(ItemData def)
    {
        if (def == null) return;

        if (string.IsNullOrEmpty(def.itemCode))
        {
            Debug.LogWarning($"InventoryController: Intentando añadir ítem '{def.name}' sin itemCode configurado.");
            return;
        }

        string k = def.itemCode.ToLowerInvariant();
        if (def.isStackable)
        {
            if (!s_PersistentBag.ContainsKey(k)) s_PersistentKeys.Add(k);
            s_PersistentBag[k] = s_PersistentBag.TryGetValue(k, out var v)
                ? (v.def, Mathf.Min(v.qty + 1, def.maxStack))
                : (def, 1);
        }
        else
        {
            int idx = 0;
            while (s_PersistentBag.ContainsKey(k + "_" + idx)) idx++;
            string sk = k + "_" + idx;
            s_PersistentBag[sk] = (def, 1);
            s_PersistentKeys.Add(sk);
        }
    }

    public static void RemoveItem(string key)
    {
        if (!s_PersistentBag.TryGetValue(key, out var slot)) return;

        if (slot.qty > 1)
            s_PersistentBag[key] = (slot.def, slot.qty - 1);
        else
        {
            s_PersistentBag.Remove(key);
            s_PersistentKeys.Remove(key);
            s_PersistentEquipped.Remove(key);
        }
    }

    void RemoveOne(string key)
    {
        if (!_bag.TryGetValue(key, out var slot)) return;
        if (slot.qty > 1) s_PersistentBag[key] = (slot.def, slot.qty - 1);
        else
        {
            s_PersistentBag.Remove(key);
            s_PersistentKeys.Remove(key);
            s_PersistentEquipped.Remove(key);
            if (_selectedIndex >= _keys.Count) _selectedIndex = _keys.Count - 1;
            _dropdownIndex = -1;
        }
    }

    void SetOpen(bool open)
    {
        _open = open;
        if (IsOwner && _playerInput != null) _playerInput.enabled = !open;

        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;

        if (!open) { _selectedIndex = -1; _dropdownIndex = -1; _dragging = false; }
    }

    void Update()
    {
        if (!IsOwner) return;

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.iKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame)
            SetOpen(!_open);

        // Optimización: Actualizar conteo de coleccionables cada 1 segundo o si está sucio
        if (s_CountDirty || Time.time > _countUpdateTimer)
        {
            RefreshCollectibleCount();
            _countUpdateTimer = Time.time + 1.0f;
            s_CountDirty = false;
        }
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
    Rect CellRectSel(int i) { var c = CellCenter(i); float h = Mathf.RoundToInt(cellSize * 1.25f) * 0.5f; return new Rect(c.x - h, c.y - h, Mathf.RoundToInt(cellSize * 1.25f), Mathf.RoundToInt(cellSize * 1.25f)); }

    Rect DropdownRectFor(int i)
    {
        Rect cell = CellRectSel(i); int dw = 130, dh = 38;
        float dx = cell.x, dy = cell.yMax + 4;
        if (dx + dw > PanelX() + panelWidth - padding) dx = cell.xMax - dw;
        if (dy + dh > PanelY() + panelHeight - padding) dy = cell.y - dh - 4;
        return new Rect(dx, dy, dw, dh);
    }

    Texture2D _texNormal, _texSelected, _texDropdown, _texPanel, _texDropZone, _texGhost;
    GUIStyle _titleSty, _qtySty, _emptySty, _badgeSty, _ddNorm, _ddHov, _dropHintSty;
    bool _stylesReady;

    void EnsureStyles() { if (_stylesReady) return; _titleSty = Sty(28, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white); _qtySty = Sty(qtyFontSize, FontStyle.Bold, TextAnchor.LowerRight, Color.white); _emptySty = Sty(18, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f)); _badgeSty = Sty(20, FontStyle.Bold, TextAnchor.UpperRight, Color.yellow); _ddNorm = Sty(15, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white); _ddHov = Sty(15, FontStyle.Bold, TextAnchor.MiddleCenter, Color.yellow); _dropHintSty = Sty(17, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.8f, 1f)); _stylesReady = true; }
    void EnsureTextures() { if (_texNormal != null) return; int s = 64; _texNormal = MakeRoundedTex(s, cornerRadius, new Color(1f, 1f, 1f, 0.10f), Color.clear, 0); _texSelected = MakeRoundedTex(s, cornerRadius, new Color(0.08f, 0.08f, 0.08f, 0.97f), new Color(1f, 0.85f, 0f, 1f), 3); _texDropdown = MakeRoundedTex(s, 8, new Color(0.10f, 0.10f, 0.10f, 0.97f), new Color(1f, 0.85f, 0f, 0.6f), 1); _texPanel = MakeRoundedTex(s, 20, new Color(0f, 0f, 0f, 0.93f), Color.clear, 0); _texDropZone = MakeRoundedTex(s, 16, new Color(0.8f, 0.2f, 0.1f, 0.55f), new Color(1f, 0.4f, 0.1f, 0.9f), 3); _texGhost = MakeRoundedTex(s, cornerRadius, new Color(1f, 1f, 1f, 0.30f), Color.clear, 0); }
    Texture2D MakeRoundedTex(int s, int r, Color fill, Color border, int bw) { var tex = new Texture2D(s, s, TextureFormat.RGBA32, false); tex.filterMode = FilterMode.Bilinear; Color clear = new Color(0, 0, 0, 0); Color[] px = new Color[s * s]; for (int y = 0; y < s; y++) for (int x = 0; x < s; x++) { float cx = Mathf.Clamp(x, r, s - 1 - r), cy = Mathf.Clamp(y, r, s - 1 - r); float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)); if (d > r + 1f) px[y * s + x] = clear; else if (d > r - 0.5f) px[y * s + x] = Color.Lerp(fill, clear, d - (r - 0.5f)); else if (bw > 0 && d > r - bw) px[y * s + x] = border; else px[y * s + x] = fill; } tex.SetPixels(px); tex.Apply(); return tex; }
    static GUIStyle Sty(int sz, FontStyle fs, TextAnchor a, Color c) { var s = new GUIStyle(GUI.skin.label) { fontSize = sz, fontStyle = fs, alignment = a }; s.normal.textColor = c; return s; }

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
            GUI.DrawTexture(new Rect(cell.x + m, cell.y + m, size - m * 2, size - m - mb), def.icon.texture, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }
        if (!ghost)
        {
            if (def.isStackable) GUI.Label(new Rect(cell.x + 2, cell.y + 2, size - 6, size - 6), "x" + slot.qty, _qtySty);
            if (equipped) GUI.Label(new Rect(cell.x + 2, cell.y + 2, size - 6, size - 6), "E", _badgeSty);
        }
    }

    void OnGUI()
    {
        if (!IsOwner || !_open) return;
        EnsureStyles();
        EnsureTextures();
        Event e = Event.current;
        Rect panel = PanelRect();
        float x0 = PanelX(), y0 = PanelY();
        Vector2 mp = e.mousePosition;

        // ... (rest of drawing logic)
        GUI.color = Color.white;
        GUI.DrawTexture(panel, _texPanel);
        GUI.Label(new Rect(x0, y0 + 8, panelWidth, titleH), "INVENTARIO", _titleSty);

        for (int i = 0; i < _keys.Count; i++) { if (i == _selectedIndex && !_dragging) continue; DrawCell(i, false, _dragging && i == _dragIndex); }
        if (_bag.Count == 0) GUI.Label(new Rect(x0, y0 + titleH, panelWidth, panelHeight - titleH), "Inventario vacio", _emptySty);
        if (!_dragging && _selectedIndex >= 0 && _selectedIndex < _keys.Count) DrawCell(_selectedIndex, true, false);
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
        if (_dragOutside)
        {
            Rect dz = new Rect(x0, y0 + panelHeight + 10, panelWidth, 60);
            GUI.color = Color.white;
            GUI.DrawTexture(dz, _texDropZone);
            GUI.Label(dz, "Suelta aqui para arrojar al mundo", _dropHintSty);
        }
        if (_dragging && _dragIndex >= 0 && _dragIndex < _keys.Count)
        {
            string key = _keys[_dragIndex];
            if (_bag.TryGetValue(key, out var slot))
            {
                int sz = cellSize;
                Rect r = new Rect(_dragPos.x - sz * 0.5f, _dragPos.y - sz * 0.5f, sz, sz);
                GUI.color = Color.white;
                GUI.DrawTexture(r, _texGhost);
                if (slot.def.icon != null) { int m = 10; GUI.color = new Color(1, 1, 1, 0.8f); GUI.DrawTexture(new Rect(r.x + m, r.y + m, sz - m * 2, sz - m * 2), slot.def.icon.texture, ScaleMode.ScaleToFit); GUI.color = Color.white; }
            }
        }
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            bool handled = false;
            if (_selectedIndex >= 0 && _selectedIndex < _keys.Count && CellRectSel(_selectedIndex).Contains(mp)) { _dragIndex = _selectedIndex; _dragging = true; _dragPos = mp; _dropdownIndex = -1; handled = true; e.Use(); }
            if (!handled) { for (int i = 0; i < _keys.Count; i++) { if (CellRect(i).Contains(mp)) { _selectedIndex = i; _dragIndex = i; _dragging = true; _dragPos = mp; _dropdownIndex = -1; handled = true; e.Use(); break; } } }
            if (!handled && _dropdownIndex >= 0 && _dropdownIndex < _keys.Count)
            {
                Rect dd = DropdownRectFor(_dropdownIndex);
                if (dd.Contains(mp)) { string key = _keys[_dropdownIndex]; if (_bag.TryGetValue(key, out var slot) && slot.def.type == ItemType.Equipment) { if (_equipped.Contains(key)) _equipped.Remove(key); else _equipped.Add(key); } _dropdownIndex = -1; handled = true; e.Use(); }
                else { _dropdownIndex = -1; }
            }
        }
        if (e.type == EventType.MouseUp && e.button == 0 && _dragging)
        {
            if (_dragOutside)
            {
                string key = _keys[_dragIndex];
                if (_bag.TryGetValue(key, out var slot))
                {
                    DropItemInWorld(slot.def);
                    RemoveItem(key);
                    _selectedIndex = -1;
                }
            }
            else { _selectedIndex = _dragIndex; string key = _keys[_dragIndex]; if (_bag.TryGetValue(key, out var slot) && slot.def.type == ItemType.Equipment) _dropdownIndex = _dragIndex; }
            _dragging = false; _dragIndex = -1; e.Use();
        }
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape) { if (_dragging) { _dragging = false; _dragIndex = -1; } else if (_dropdownIndex >= 0) { _dropdownIndex = -1; } else if (_selectedIndex >= 0) { _selectedIndex = -1; _dropdownIndex = -1; } else { SetOpen(false); } e.Use(); }
        }
    }

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    public static int GetCollectiblesRemaining() => s_CollectiblesRemaining;

    public static Dictionary<string, (ItemData def, int qty)> GetBag() => s_PersistentBag;
    public static List<string> GetKeys() => s_PersistentKeys;

    // Facade for CraftingManager to get item data by code
    public static ItemData GetItemDataByCode(string code)
    {
        foreach(var item in s_PersistentBag.Values)
        {
            if (item.def.itemCode.ToLowerInvariant() == code.ToLowerInvariant()) return item.def;
        }
        return null;
    }

    public static void RefreshCollectibleCount()
    {
        // Optimización: FindObjects es lento, lo hacemos solo cuando sea necesario
        var activos = FindObjectsByType<PickupController>(FindObjectsSortMode.None);
        s_CollectiblesRemaining = activos.Length;
    }

    public static void MarkCountDirty() => s_CountDirty = true;

    private void DropItemInWorld(ItemData item)
    {
        if (item == null || item.worldPrefab == null)
        {
            Debug.LogWarning($"[Inventory] No se puede soltar {item?.displayName}: Prefab no asignado.");
            return;
        }

        Vector3 dropPos = transform.position + transform.forward * dropDistance + Vector3.up * 0.5f;
        GameObject spawned = Instantiate(item.worldPrefab, dropPos, Quaternion.identity);

        // Añadir mensaje visual usando la lógica de SpawnController si existe
        if (spawned.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.AddForce(transform.forward * 2f, ForceMode.Impulse);
        }

        // Registrar en red si es necesario
        if (IsNetworkActive && IsServer)
        {
            if (spawned.TryGetComponent<NetworkObject>(out var netObj)) netObj.Spawn();
        }

        MarkCountDirty();
    }
}
