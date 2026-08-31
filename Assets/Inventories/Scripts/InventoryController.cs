using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Combating.Scripts;

[RequireComponent(typeof(SpawnController))]
public class InventoryController : NetworkBehaviour
{
    public static InventoryController Instance { get; private set; }

    private static Dictionary<string, (ItemData def, int qty)> s_PersistentBag = new();
    private static List<string> s_PersistentKeys = new();
    private static HashSet<string> s_PersistentEquipped = new();

    [Header("Panel Settings")]
    public int panelWidth = 700;
    public int panelHeight = 550;
    public int columns = 6;
    public int cellSize = 90;
    public int padding = 20;
    public int titleH = 65;
    public int qtyFontSize = 14;
    public int cornerRadius = 15;
    public Color panelColor = new Color(0.05f, 0.05f, 0.05f, 0.95f);
    public Color accentColor = new Color(1f, 0.85f, 0f, 1f);

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
    int _stickyIndex = -1;
    bool _open;
    int _dragIndex = -1;
    Vector2 _dragPos;
    bool _dragging = false;
    bool _dragOutside = false;

    PlayerInput _playerInput;
    SpawnController _spawnController;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            Instance = this;
            _playerInput = GetComponent<PlayerInput>();
            _spawnController = GetComponent<SpawnController>();
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
            if (_stickyIndex >= _keys.Count) _stickyIndex = -1;
        }
    }

    void SetOpen(bool open)
    {
        _open = open;
        if (IsOwner && _playerInput != null) _playerInput.enabled = !open;

        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;

        if (!open) { _selectedIndex = -1; _stickyIndex = -1; _dragging = false; }
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

    Texture2D _texNormal, _texSelected, _texPanel, _texDropZone, _texGhost, _texBtnClose, _texBtnPlus, _texBtnMinus;
    GUIStyle _titleSty, _qtySty, _emptySty, _badgeSty, _ddNorm, _ddHov, _dropHintSty, _btnSty;
    bool _stylesReady;

    void EnsureStyles() { if (_stylesReady) return; _titleSty = Sty(32, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white); _qtySty = Sty(qtyFontSize, FontStyle.Bold, TextAnchor.LowerRight, accentColor); _emptySty = Sty(18, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f)); _badgeSty = Sty(20, FontStyle.Bold, TextAnchor.UpperRight, accentColor); _ddNorm = Sty(15, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white); _ddHov = Sty(15, FontStyle.Bold, TextAnchor.MiddleCenter, accentColor); _dropHintSty = Sty(17, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.8f, 1f)); _btnSty = Sty(20, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white); _stylesReady = true; }
    void EnsureTextures() { if (_texNormal != null) return; int s = 64; _texNormal = MakeRoundedTex(s, 8, new Color(1f, 1f, 1f, 0.08f), Color.clear, 0); _texSelected = MakeRoundedTex(s, 8, new Color(1f, 1f, 1f, 0.15f), accentColor, 2); _texPanel = MakeRoundedTex(s, cornerRadius, panelColor, Color.clear, 0); _texDropZone = MakeRoundedTex(s, 16, new Color(0.8f, 0.2f, 0.1f, 0.55f), new Color(1f, 0.4f, 0.1f, 0.9f), 3); _texGhost = MakeRoundedTex(s, 8, new Color(1f, 1f, 1f, 0.30f), Color.clear, 0); _texBtnClose = MakeRoundedTex(s, 8, new Color(0.8f, 0.1f, 0.1f, 0.9f), Color.white, 2); _texBtnPlus = MakeRoundedTex(s, 4, new Color(0.1f, 0.6f, 0.1f, 0.9f), Color.white, 1); _texBtnMinus = MakeRoundedTex(s, 4, new Color(0.6f, 0.1f, 0.1f, 0.9f), Color.white, 1); }
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
            GUI.color = ghost ? new Color(1, 1, 1, 0.4f) : Color.white;
            // Icono ajustado para no solaparse con los nuevos botones DROP/EQUIP
            GUI.DrawTexture(new Rect(cell.x + 12, cell.y + 24, size - 24, size - 48), def.icon.texture, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }
        if (!ghost)
        {
            if (def.isStackable) GUI.Label(new Rect(cell.x + 2, cell.y + 2, size - 6, size - 6), "x" + slot.qty, _qtySty);
            if (equipped) GUI.Label(new Rect(cell.x + 2, cell.y + 2, size - 6, size - 6), "E", _badgeSty);

            // Botones DROP / EQUIP - Solo se muestran si el item está seleccionado (hover o click)
            if (selected)
            {
                float bH = 24;
                float bW = size;
                Rect rEquip = new Rect(cell.x, cell.y, bW, bH);
                Rect rDrop = new Rect(cell.x, cell.yMax - bH, bW, bH);

                if (def.type == ItemType.Equipment)
                {
                    GUI.DrawTexture(rEquip, _texBtnPlus);
                    GUI.Label(rEquip, _equipped.Contains(key) ? "UNEQUIP" : "EQUIP", _btnSty);
                }

                GUI.DrawTexture(rDrop, _texBtnMinus);
                GUI.Label(rDrop, "DROP", _btnSty);
            }
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

        // Reset hover for current frame
        int hoverIndex = -1;
        _dragOutside = false;

        GUI.color = Color.white;
        GUI.DrawTexture(panel, _texPanel);
        GUI.Label(new Rect(x0, y0 + 8, panelWidth, titleH), "INVENTARIO", _titleSty);

        // 1. Detectar Hover (con Histeresis: si ya está seleccionado, el área de detección es mayor)
        if (!_dragging && panel.Contains(mp))
        {
            for (int i = 0; i < _keys.Count; i++)
            {
                // Si i es el activo actual, usamos el rect expandido para que el mouse llegue a los botones
                Rect hitRect = (i == _selectedIndex) ? CellRectSel(i) : CellRect(i);
                if (hitRect.Contains(mp)) { hoverIndex = i; break; }
            }
        }

        // El item seleccionado visualmente es el que tiene hover O el que está "pegado" (sticky)
        _selectedIndex = (hoverIndex != -1) ? hoverIndex : _stickyIndex;

        // Botón cerrar X
        if (GUI.Button(new Rect(x0 + panelWidth - 50, y0 + 15, 35, 35), "X", _btnSty)) SetOpen(false);

        // Mensaje Centralizado de Coleccionables
        string collText = $"COLECTABLES EN BIOMA: {s_CollectiblesRemaining}";
        GUI.Label(new Rect(x0 + padding, y0 + panelHeight - 35, panelWidth - padding * 2, 25), collText, _ddNorm);

        // Dibujar celdas
        for (int i = 0; i < _keys.Count; i++)
        {
            if (i == _selectedIndex && !_dragging) continue;
            DrawCell(i, false, _dragging && i == _dragIndex);
        }

        if (_bag.Count == 0) GUI.Label(new Rect(x0, y0 + titleH, panelWidth, panelHeight - titleH), "Inventario vacio", _emptySty);

        // Dibujar la celda seleccionada encima
        if (!_dragging && _selectedIndex >= 0 && _selectedIndex < _keys.Count)
            DrawCell(_selectedIndex, true, false);

        // Lógica de Dragging
        if (_dragging && _dragIndex >= 0 && _dragIndex < _keys.Count)
        {
            _dragOutside = !panel.Contains(mp);
            _dragPos = mp;
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

        if (_dragOutside)
        {
            Rect dz = new Rect(mp.x - 100, mp.y + 20, 200, 40);
            GUI.color = new Color(1, 1, 1, 0.7f);
            GUI.DrawTexture(dz, _texDropZone);
            GUI.Label(dz, "Soltar objeto", _dropHintSty);
            GUI.color = Color.white;
        }

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            bool handled = false;

            // Primero chequear si el click fue en un botón del item seleccionado (hover o sticky)
            if (!handled && _selectedIndex >= 0 && _selectedIndex < _keys.Count)
            {
                Rect cell = CellRectSel(_selectedIndex);
                float bH = 24; float bW = cell.width;
                Rect rEquip = new Rect(cell.x, cell.y, bW, bH);
                Rect rDrop = new Rect(cell.x, cell.yMax - bH, bW, bH);
                string key = _keys[_selectedIndex];
                var slot = _bag[key];

                if (slot.def.type == ItemType.Equipment && rEquip.Contains(mp))
                {
                    if (_equipped.Contains(key)) _equipped.Remove(key); else _equipped.Add(key);
                    handled = true; e.Use();
                }
                else if (rDrop.Contains(mp))
                {
                    DropItemInWorld(slot.def);
                    RemoveOne(key);
                    handled = true; e.Use();
                }
            }

            // Si no fue en un botón, ver si fue en una celda para hacerla "sticky"
            if (!handled)
            {
                int clickedCell = -1;
                for (int i = 0; i < _keys.Count; i++)
                {
                    if (CellRect(i).Contains(mp)) { clickedCell = i; break; }
                }

                if (clickedCell != -1)
                {
                    _stickyIndex = clickedCell;
                    _dragIndex = clickedCell;
                    _dragging = true;
                    _dragPos = mp;
                    handled = true;
                    e.Use();
                }
                else
                {
                    // Click fuera de celdas y botones: limpiar sticky
                    _stickyIndex = -1;
                }
            }
        }

        if (e.type == EventType.MouseUp && e.button == 0 && _dragging)
        {
            if (_dragOutside)
            {
                string key = _keys[_dragIndex];
                if (_bag.TryGetValue(key, out var slot))
                {
                    DropItemInWorld(slot.def, mp);
                    RemoveItem(key);
                }
                _stickyIndex = -1;
            }
            _dragging = false; _dragIndex = -1; e.Use();
        }

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                if (_dragging) { _dragging = false; _dragIndex = -1; }
                else if (_stickyIndex >= 0) { _stickyIndex = -1; }
                else { SetOpen(false); }
                e.Use();
            }
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
        // Optimización: Usamos el contador estático en lugar de FindObjectsByType para evitar picos de CPU (Starvation)
        s_CollectiblesRemaining = PickupController.ActiveCount;
    }

    public static void MarkCountDirty() => s_CountDirty = true;

    private void DropItemInWorld(ItemData item, Vector2? mousePos = null)
    {
        if (item == null || item.worldPrefab == null)
        {
            Debug.LogWarning($"[Inventory] No se puede soltar {item?.displayName}: Prefab no asignado.");
            return;
        }

        Vector3 offset = Vector3.zero;
        Vector3 impulse = Vector3.zero;

        if (mousePos.HasValue)
        {
            // Drag Drop: Dirección desde el centro de la pantalla hacia el ratón
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dir2D = (mousePos.Value - screenCenter).normalized;

            Transform cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null)
            {
                Vector3 camFwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                Vector3 camRight = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
                // Invertimos Y porque en GUI el origen es arriba, pero en world queremos "adelante"
                Vector3 worldDir = (camFwd * -dir2D.y + camRight * dir2D.x).normalized;
                offset = worldDir * 2.0f;
                impulse = worldDir * 5f;
            }
            else
            {
                offset = new Vector3(dir2D.x, 0, -dir2D.y).normalized * 2.0f;
                impulse = offset.normalized * 5f;
            }
        }
        else
        {
            // Button Drop: Dirección aleatoria
            Vector2 randomDir = Random.insideUnitCircle.normalized;
            offset = new Vector3(randomDir.x, 0, randomDir.y) * 1.8f;
            impulse = offset.normalized * 4f;
        }

        Vector3 dropPos = transform.position + offset;
        dropPos.y = transform.position.y + 0.1f; // Mantener altura del jugador (ligeramente elevado para evitar clips)

        if (_spawnController != null)
        {
            RequestDropItemServerRpc(item.itemCode, dropPos, impulse);
        }

        MarkCountDirty();
    }

    [ServerRpc]
    private void RequestDropItemServerRpc(string itemCode, Vector3 dropPos, Vector3 impulse)
    {
        ItemData data = GetItemDataByCode(itemCode);
        if (data == null || data.worldPrefab == null) return;

        if (_spawnController != null)
        {
            _spawnController.SpawnSingleItem(data.worldPrefab, dropPos, $"-1 {data.displayName}", impulse);
        }
        else
        {
            // Fallback si no hay spawn controller en el servidor
            GameObject spawned = Instantiate(data.worldPrefab, dropPos, Quaternion.identity);
            if (spawned.TryGetComponent<Rigidbody>(out var rb)) rb.AddForce(impulse, ForceMode.Impulse);
            if (spawned.TryGetComponent<NetworkObject>(out var netObj)) netObj.Spawn();
        }
    }
}
