using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Combating.Scripts;
using Crafting.Scripts;

// Estructura para sincronización de red
public struct NetworkInventorySlot : INetworkSerializable, IEquatable<NetworkInventorySlot>
{
    public int itemId;
    public int quantity;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemId);
        serializer.SerializeValue(ref quantity);
    }

    public bool Equals(NetworkInventorySlot other)
    {
        return itemId == other.itemId && quantity == other.quantity;
    }

    public override bool Equals(object obj)
    {
        return obj is NetworkInventorySlot other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + itemId.GetHashCode();
            hash = hash * 23 + quantity.GetHashCode();
            return hash;
        }
    }
}

[RequireComponent(typeof(SpawnController))]
public class InventoryController : NetworkBehaviour
{
    public static InventoryController LocalInstance { get; private set; }

    [Header("Network Data")]
    public NetworkList<NetworkInventorySlot> NetworkBag;

    // Almacenamiento para modo offline
    private List<NetworkInventorySlot> _offlineBag = new List<NetworkInventorySlot>();

    // Cache local para acceso rápido (se alimenta de NetworkBag o _offlineBag)
    private Dictionary<string, (ItemData def, int qty)> _localBag = new();
    private List<string> _localKeys = new();

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

    [Header("Database & Settings")]
    public List<ItemData> itemDatabase;
    public float dropDistance = 3.5f;

    private static int s_CollectiblesRemaining = 0;
    private static bool s_CountDirty = true;
    private float _countUpdateTimer = 0f;

    private bool _open;
    private ItemData _draggedItem;

    // GUI Resources
    private Texture2D _texNormal, _texSelected, _texPanel, _texBtn;
    private GUIStyle _titleSty, _qtySty, _emptySty, _btnSty;
    private bool _stylesReady;

    PlayerInput _playerInput;
    SpawnController _spawnController;
    CostumeController _costumeController;

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;

    private void Awake()
    {
        NetworkBag = new NetworkList<NetworkInventorySlot>();

        // Inicialización offline
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            LocalInstance = this;
            _playerInput = GetComponent<UnityEngine.InputSystem.PlayerInput>();
            _spawnController = GetComponent<Combating.Scripts.SpawnController>();
            _costumeController = GetComponent<CostumeController>();
            if (_costumeController == null) _costumeController = gameObject.AddComponent<CostumeController>();
        }
    }

    public override void OnNetworkSpawn()
    {
        _playerInput = GetComponent<PlayerInput>();
        _spawnController = GetComponent<SpawnController>();
        _costumeController = GetComponent<CostumeController>();
        if (_costumeController == null) _costumeController = gameObject.AddComponent<CostumeController>();

        if (IsOwner) LocalInstance = this;

        NetworkBag.OnListChanged += (changeEvent) => RefreshLocalCache();
        RefreshLocalCache();
    }

    private void RefreshLocalCache()
    {
        _localBag.Clear();
        _localKeys.Clear();

        if (IsNetworkActive)
        {
            foreach (var slot in NetworkBag) ProcessSlot(slot);
        }
        else
        {
            foreach (var slot in _offlineBag) ProcessSlot(slot);
        }
    }

    private void ProcessSlot(NetworkInventorySlot slot)
    {
        ItemData data = GetItemDataById(slot.itemId);
        if (data != null)
        {
            string key = data.itemCode.ToLowerInvariant();
            _localBag[key] = (data, slot.quantity);
            if (!_localKeys.Contains(key)) _localKeys.Add(key);
        }
    }

    public ItemData GetItemDataById(int id)
    {
        if (itemDatabase == null || itemDatabase.Count == 0)
            itemDatabase = Resources.LoadAll<ItemData>("").ToList();

        var found = itemDatabase.FirstOrDefault(x => x.itemId == id);
        if (found == null)
        {
            var allItems = Resources.LoadAll<ItemData>("");
            found = allItems.FirstOrDefault(x => x.itemId == id);
            if (found != null && !itemDatabase.Contains(found)) itemDatabase.Add(found);
        }
        return found;
    }

    public ItemData GetItemDataByCode(string code)
    {
        if (itemDatabase == null) return null;
        return itemDatabase.FirstOrDefault(x => x.itemCode.ToLowerInvariant() == code.ToLowerInvariant());
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AddItemServerRpc(int itemId, int qty)
    {
        InternalAddItem(itemId, qty);
    }

    public void InternalAddItem(int itemId, int qty)
    {
        if (IsNetworkActive)
        {
            for (int i = 0; i < NetworkBag.Count; i++)
            {
                if (NetworkBag[i].itemId == itemId)
                {
                    var slot = NetworkBag[i];
                    slot.quantity += qty;
                    NetworkBag[i] = slot;
                    return;
                }
            }
            NetworkBag.Add(new NetworkInventorySlot { itemId = itemId, quantity = qty });
        }
        else
        {
            // Lógica Offline
            for (int i = 0; i < _offlineBag.Count; i++)
            {
                if (_offlineBag[i].itemId == itemId)
                {
                    var slot = _offlineBag[i];
                    slot.quantity += qty;
                    _offlineBag[i] = slot;
                    RefreshLocalCache();
                    return;
                }
            }
            _offlineBag.Add(new NetworkInventorySlot { itemId = itemId, quantity = qty });
            RefreshLocalCache();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RemoveItemServerRpc(int itemId, int qty)
    {
        InternalRemoveItem(itemId, qty);
    }

    private void InternalRemoveItem(int itemId, int qty)
    {
        if (IsNetworkActive)
        {
            for (int i = 0; i < NetworkBag.Count; i++)
            {
                if (NetworkBag[i].itemId == itemId)
                {
                    var slot = NetworkBag[i];
                    slot.quantity -= qty;
                    if (slot.quantity <= 0) NetworkBag.RemoveAt(i);
                    else NetworkBag[i] = slot;
                    return;
                }
            }
        }
        else
        {
            // Lógica Offline
            for (int i = 0; i < _offlineBag.Count; i++)
            {
                if (_offlineBag[i].itemId == itemId)
                {
                    var slot = _offlineBag[i];
                    slot.quantity -= qty;
                    if (slot.quantity <= 0) _offlineBag.RemoveAt(i);
                    else _offlineBag[i] = slot;
                    RefreshLocalCache();
                    return;
                }
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void DropItemServerRpc(int itemId, Vector3 position)
    {
        ItemData data = GetItemDataById(itemId);
        if (data != null)
        {
            InternalRemoveItem(itemId, 1);
            if (_spawnController != null)
            {
                _spawnController.SpawnSingleItem(data.worldPrefab, position + Vector3.up * 0.5f, data.displayName);
            }
        }
    }

    public static void Add(ItemData def)
    {
        if (LocalInstance == null) return;
        if (LocalInstance.IsNetworkActive) LocalInstance.AddItemServerRpc(def.itemId, 1);
        else LocalInstance.InternalAddItem(def.itemId, 1);
    }

    public static void RemoveItem(string key) {
        var data = LocalInstance?.GetItemDataByCode(key);
        if (data != null)
        {
            if (LocalInstance.IsNetworkActive) LocalInstance.RemoveItemServerRpc(data.itemId, 1);
            else LocalInstance.InternalRemoveItem(data.itemId, 1);
        }
    }

    private void Update()
    {
        if (!CanExecuteLocalLogic) return;
        if (Keyboard.current != null && (Keyboard.current.iKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame))
            SetOpen(!_open);

        if (s_CountDirty || Time.time > _countUpdateTimer)
        {
            s_CollectiblesRemaining = PickupController.ActiveCount;
            _countUpdateTimer = Time.time + 1.0f;
            s_CountDirty = false;
        }
    }

    private void SetOpen(bool open)
    {
        _open = open;
        if (_playerInput != null) _playerInput.enabled = !open;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
        if (!open) _draggedItem = null;
    }

    private void OnGUI()
    {
        if (!CanExecuteLocalLogic || !_open) return;
        if (Crafting.Scripts.CraftingManager.Instance != null && Crafting.Scripts.CraftingManager.Instance.IsUIOpen) return;

        EnsureStyles();
        Rect panelRect = new Rect((Screen.width - panelWidth) / 2f, (Screen.height - panelHeight) / 2f, panelWidth, panelHeight);
        DrawInventoryUI(panelRect, "MI INVENTARIO");

        if (_draggedItem != null)
        {
            Vector2 mousePos = Event.current.mousePosition;
            Rect dragRect = new Rect(mousePos.x - cellSize/2, mousePos.y - cellSize/2, cellSize, cellSize);
            if (_draggedItem.icon != null) GUI.DrawTexture(dragRect, _draggedItem.icon.texture);

            if (Event.current.type == EventType.MouseUp)
            {
                if (!panelRect.Contains(mousePos)) DropItem(_draggedItem);
                _draggedItem = null;
            }
        }
    }

    public void DrawInventoryUI(Rect panel, string title)
    {
        EnsureStyles();
        GUI.DrawTexture(panel, _texPanel);
        GUI.Label(new Rect(panel.x, panel.y + 10, panel.width, titleH), title, _titleSty);

        if (GUI.Button(new Rect(panel.xMax - 50, panel.y + 15, 35, 35), "X", _btnSty)) SetOpen(false);

        int i = 0;
        var keysCopy = _localKeys.ToArray();
        foreach (var key in keysCopy)
        {
            if (!_localBag.TryGetValue(key, out var slot)) continue;

            Rect cell = new Rect(panel.x + padding + (i % columns) * (cellSize + 10),
                                 panel.y + titleH + (i / columns) * (cellSize + 40),
                                 cellSize, cellSize);

            bool isOver = cell.Contains(Event.current.mousePosition);
            GUI.DrawTexture(cell, isOver ? _texSelected : _texNormal);

            if (slot.def.icon != null && slot.def.icon.texture != null)
                GUI.DrawTexture(new Rect(cell.x + 10, cell.y + 10, cell.width - 20, cell.height - 20), slot.def.icon.texture);

            GUI.Label(cell, "x" + slot.qty, _qtySty);

            Rect btnArea = new Rect(cell.x, cell.yMax + 2, cell.width, 35);
            string actionText = (slot.def.type == ItemType.Costume && _costumeController != null && _costumeController.IsWearing(slot.def.itemId)) ? "QUITAR" : (slot.def.type == ItemType.Costume ? "EQUIPAR" : "USAR");

            if (GUI.Button(new Rect(btnArea.x, btnArea.y, btnArea.width * 0.5f, 30), actionText, _btnSty)) UseItem(slot.def);
            if (GUI.Button(new Rect(btnArea.x + btnArea.width * 0.5f, btnArea.y, btnArea.width * 0.5f, 30), "DROP", _btnSty)) DropItem(slot.def);

            if (isOver && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _draggedItem = slot.def;
                Event.current.Use();
            }
            i++;
        }

        if (_localKeys.Count == 0)
            GUI.Label(new Rect(panel.x, panel.y + titleH, panel.width, panel.height - titleH), "Inventario Vacío", _emptySty);
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _texNormal = MakeRoundedTex(64, 8, new Color(1f, 1f, 1f, 0.08f), Color.clear, 0);
        _texSelected = MakeRoundedTex(64, 8, new Color(1f, 1f, 1f, 0.15f), accentColor, 2);
        _texPanel = MakeRoundedTex(64, cornerRadius, panelColor, Color.clear, 0);
        _titleSty = Sty(32, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        _qtySty = Sty(qtyFontSize, FontStyle.Bold, TextAnchor.LowerRight, accentColor);
        _emptySty = Sty(18, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.5f));
        _btnSty = new GUIStyle(GUI.skin.button) { fontSize = 10, fontStyle = FontStyle.Bold };
        _btnSty.normal.textColor = Color.white;
        _stylesReady = true;
    }

    private Texture2D MakeRoundedTex(int s, int r, Color fill, Color border, int bw)
    {
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        Color[] px = new Color[s * s];
        for (int y = 0; y < s; y++) {
            for (int x = 0; x < s; x++) {
                float cx = Mathf.Clamp(x, r, s - 1 - r), cy = Mathf.Clamp(y, r, s - 1 - r);
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d > r + 0.5f) px[y * s + x] = Color.clear;
                else if (bw > 0 && d > r - bw) px[y * s + x] = border;
                else px[y * s + x] = fill;
            }
        }
        tex.SetPixels(px); tex.Apply(); return tex;
    }

    private static GUIStyle Sty(int sz, FontStyle fs, TextAnchor a, Color c) {
        var s = new GUIStyle(GUI.skin.label) { fontSize = sz, fontStyle = fs, alignment = a };
        s.normal.textColor = c; return s;
    }

    public void UseItem(ItemData item)
    {
        if (item == null || _costumeController == null) return;
        if (item.type == ItemType.Costume)
        {
            if (_costumeController.IsWearing(item.itemId))
            {
                if (IsNetworkActive) _costumeController.RequestRestoreDefaultServerRpc();
                else _costumeController.RestoreDefaultLocal();
            }
            else
            {
                if (IsNetworkActive) _costumeController.RequestCostumeChangeServerRpc(item.itemId);
                else _costumeController.ApplyCostumeLocal(item.worldPrefab, item.itemId);
            }
        }
        else if (item.isUsable) RemoveItemServerRpc(item.itemId, 1);
    }

    public void DropItem(ItemData item)
    {
        if (item == null) return;
        Vector3 dropPos = transform.position + transform.forward * dropDistance;
        if (IsNetworkActive) DropItemServerRpc(item.itemId, dropPos);
        else
        {
            InternalRemoveItem(item.itemId, 1);
            if (_spawnController != null) _spawnController.SpawnSingleItem(item.worldPrefab, dropPos, item.displayName);
        }
    }

    public static Dictionary<string, (ItemData def, int qty)> GetBag() => LocalInstance?._localBag ?? new();
    public static List<string> GetKeys() => LocalInstance?._localKeys ?? new();
    public static void MarkCountDirty() => s_CountDirty = true;
    public static ItemData GetItemDataByCodeStatic(string code) => LocalInstance?.GetItemDataByCode(code);

    public override void OnDestroy() {
        base.OnDestroy();
        if (LocalInstance == this) LocalInstance = null;
    }
}
