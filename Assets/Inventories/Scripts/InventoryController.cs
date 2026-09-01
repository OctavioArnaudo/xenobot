using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Combating.Scripts;

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

    // Cache local para acceso rápido
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
    public float dropDistance = 2.5f;

    private static int s_CollectiblesRemaining = 0;
    private static bool s_CountDirty = true;
    private float _countUpdateTimer = 0f;

    private bool _open;

    // GUI Resources
    private Texture2D _texNormal, _texSelected, _texPanel;
    private GUIStyle _titleSty, _qtySty, _emptySty, _btnSty;
    private bool _stylesReady;

    PlayerInput _playerInput;
    SpawnController _spawnController;

    private void Awake()
    {
        NetworkBag = new NetworkList<NetworkInventorySlot>();
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[Inventory] Spawning. Database size: {(itemDatabase != null ? itemDatabase.Count : "NULL")}");

        if (IsOwner)
        {
            LocalInstance = this;
            _playerInput = GetComponent<PlayerInput>();
            _spawnController = GetComponent<SpawnController>();
        }

        NetworkBag.OnListChanged += (changeEvent) => {
            Debug.Log($"[Inventory] List changed! Count: {NetworkBag.Count}");
            RefreshLocalCache();
        };
        RefreshLocalCache();
    }

    private void RefreshLocalCache()
    {
        _localBag.Clear();
        _localKeys.Clear();
        foreach (var slot in NetworkBag)
        {
            ItemData data = GetItemDataById(slot.itemId);
            if (data != null)
            {
                string key = data.itemCode.ToLowerInvariant();
                _localBag[key] = (data, slot.quantity);
                if (!_localKeys.Contains(key)) _localKeys.Add(key);
            }
        }
    }

    public ItemData GetItemDataById(int id)
    {
        if (itemDatabase == null) return null;
        return itemDatabase.FirstOrDefault(x => x.itemId == id);
    }

    public ItemData GetItemDataByCode(string code)
    {
        if (itemDatabase == null) return null;
        return itemDatabase.FirstOrDefault(x => x.itemCode.ToLowerInvariant() == code.ToLowerInvariant());
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AddItemServerRpc(int itemId, int qty)
    {
        Debug.Log($"[Inventory Server] Adding item ID {itemId} x{qty} to bag of {OwnerClientId}");
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

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RemoveItemServerRpc(int itemId, int qty)
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

    public static void Add(ItemData def)
    {
        if (LocalInstance != null && def != null) LocalInstance.AddItemServerRpc(def.itemId, 1);
    }

    public static void RemoveItem(string key)
    {
        if (LocalInstance != null)
        {
            var data = LocalInstance.GetItemDataByCode(key);
            if (data != null) LocalInstance.RemoveItemServerRpc(data.itemId, 1);
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (Keyboard.current != null && (Keyboard.current.iKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame))
        {
            SetOpen(!_open);
        }

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
    }

    private void OnGUI()
    {
        if (!IsOwner || !_open) return;

        // Evitar solapamiento con Crafting
        if (Crafting.Scripts.CraftingManager.Instance != null && Crafting.Scripts.CraftingManager.Instance.IsUIOpen) return;

        Rect panelRect = new Rect((Screen.width - panelWidth) / 2f, (Screen.height - panelHeight) / 2f, panelWidth, panelHeight);
        DrawInventoryUI(panelRect, "MI INVENTARIO");
    }

    public void DrawInventoryUI(Rect panel, string title)
    {
        EnsureStyles();

        GUI.DrawTexture(panel, _texPanel);
        GUI.Label(new Rect(panel.x, panel.y + 10, panel.width, titleH), title, _titleSty);

        if (IsOwner && _open && (Crafting.Scripts.CraftingManager.Instance == null || !Crafting.Scripts.CraftingManager.Instance.IsUIOpen))
        {
            if (GUI.Button(new Rect(panel.xMax - 50, panel.y + 15, 35, 35), "X", _btnSty)) SetOpen(false);
        }

        int i = 0;
        foreach (var key in _localKeys)
        {
            if (!_localBag.TryGetValue(key, out var slot)) continue;

            Rect cell = new Rect(panel.x + padding + (i % columns) * (cellSize + 5),
                                 panel.y + titleH + (i / columns) * (cellSize + 5),
                                 cellSize, cellSize);

            bool isSelected = cell.Contains(Event.current.mousePosition);
            GUI.DrawTexture(cell, isSelected ? _texSelected : _texNormal);

            if (slot.def.icon != null)
                GUI.DrawTexture(new Rect(cell.x + 10, cell.y + 10, cell.width - 20, cell.height - 20), slot.def.icon.texture);

            GUI.Label(cell, "x" + slot.qty, _qtySty);
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
        _btnSty = Sty(20, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
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

    public static Dictionary<string, (ItemData def, int qty)> GetBag() => LocalInstance?._localBag ?? new();
    public static List<string> GetKeys() => LocalInstance?._localKeys ?? new();
    public static void MarkCountDirty() => s_CountDirty = true;
    public static ItemData GetItemDataByCodeStatic(string code) => LocalInstance?.GetItemDataByCode(code);

    public override void OnDestroy() {
        base.OnDestroy();
        if (LocalInstance == this) LocalInstance = null;
    }
}
