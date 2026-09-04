using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Combating.Scripts;
using Crafting.Scripts;

namespace Crafting.Scripts
{
    [RequireComponent(typeof(SpawnController))]
    public class ItemsController : NetworkBehaviour
    {
        public static ItemsController LocalInstance { get; private set; }

        [Header("Network Data")]
        public NetworkList<NetworkInventorySlot> NetworkBag;

        public NetworkVariable<int> EquippedWeaponHash = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private List<NetworkInventorySlot> _offlineBag = new List<NetworkInventorySlot>();
        private Dictionary<string, (ItemData def, int qty)> _localBag = new();
        private List<string> _localKeys = new();
        private Dictionary<int, GameObject> _equippedInstances = new();

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

        private Texture2D _texNormal, _texSelected, _texPanel, _texBtn;
        private GUIStyle _titleSty, _qtySty, _emptySty, _btnSty;
        private bool _stylesReady;

        PlayerInput _playerInput;
        SpawnController _spawnController;
        Combating.Scripts.FuelController _health;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;

        private void Awake()
        {
            NetworkBag = new NetworkList<NetworkInventorySlot>();
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                LocalInstance = this;
                InitializeComponents();
            }
        }

        public override void OnNetworkSpawn()
        {
            InitializeComponents();
            if (IsOwner) LocalInstance = this;
            NetworkBag.OnListChanged += (changeEvent) => RefreshLocalCache();
            RefreshLocalCache();
        }

        private void InitializeComponents()
        {
            _playerInput = GetComponent<PlayerInput>();
            _spawnController = GetComponent<SpawnController>();
            _health = GetComponent<Combating.Scripts.FuelController>();
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
            ItemData data = GetItemDataByHash(slot.itemHash);
            if (data != null)
            {
                string key = data.itemCode.ToLowerInvariant();
                _localBag[key] = (data, slot.quantity);
                if (!_localKeys.Contains(key)) _localKeys.Add(key);
            }
        }

        public ItemData GetItemDataByHash(int hash)
        {
            EnsureDatabase();
            var found = itemDatabase.FirstOrDefault(x => x.GetItemHashCode() == hash);
            if (found == null)
            {
                var allItems = Resources.LoadAll<ItemData>("");
                found = allItems.FirstOrDefault(x => x.GetItemHashCode() == hash);
                if (found != null && !itemDatabase.Contains(found)) itemDatabase.Add(found);
            }
            return found;
        }

        public ItemData GetItemDataByCode(string code)
        {
            EnsureDatabase();
            string c = code.ToLowerInvariant();
            return itemDatabase.FirstOrDefault(x => x.itemCode.ToLowerInvariant() == c);
        }

        private void EnsureDatabase()
        {
            if (itemDatabase == null || itemDatabase.Count == 0)
                itemDatabase = Resources.LoadAll<ItemData>("").ToList();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AddItemServerRpc(int hash, int qty) => InternalAddItem(hash, qty);

        public void InternalAddItem(int hash, int qty)
        {
            if (IsNetworkActive)
            {
                for (int i = 0; i < NetworkBag.Count; i++)
                {
                    if (NetworkBag[i].itemHash == hash)
                    {
                        var slot = NetworkBag[i];
                        slot.quantity += qty;
                        NetworkBag[i] = slot;
                        return;
                    }
                }
                NetworkBag.Add(new NetworkInventorySlot { itemHash = hash, quantity = qty });
            }
            else
            {
                for (int i = 0; i < _offlineBag.Count; i++)
                {
                    if (_offlineBag[i].itemHash == hash)
                    {
                        var slot = _offlineBag[i];
                        slot.quantity += qty;
                        _offlineBag[i] = slot;
                        RefreshLocalCache();
                        return;
                    }
                }
                _offlineBag.Add(new NetworkInventorySlot { itemHash = hash, quantity = qty });
                RefreshLocalCache();
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RemoveItemServerRpc(int hash, int qty) => InternalRemoveItem(hash, qty);

        private void InternalRemoveItem(int hash, int qty)
        {
            if (IsNetworkActive)
            {
                for (int i = 0; i < NetworkBag.Count; i++)
                {
                    if (NetworkBag[i].itemHash == hash)
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
                for (int i = 0; i < _offlineBag.Count; i++)
                {
                    if (_offlineBag[i].itemHash == hash)
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

        public static void Add(ItemData def)
        {
            if (LocalInstance == null) return;
            int hash = def.GetItemHashCode();
            if (LocalInstance.IsNetworkActive) LocalInstance.AddItemServerRpc(hash, 1);
            else LocalInstance.InternalAddItem(hash, 1);
        }

        public static void RemoveItem(string key)
        {
            var data = LocalInstance?.GetItemDataByCode(key);
            if (data != null)
            {
                int hash = data.GetItemHashCode();
                if (LocalInstance.IsNetworkActive) LocalInstance.RemoveItemServerRpc(hash, 1);
                else LocalInstance.InternalRemoveItem(hash, 1);
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
        }

        private void OnGUI()
        {
            if (!CanExecuteLocalLogic || !_open) return;
            if (CraftingManager.Instance != null && CraftingManager.Instance.IsUIOpen) return;

            EnsureStyles();
            Rect panelRect = new Rect((Screen.width - panelWidth) / 2f, (Screen.height - panelHeight) / 2f, panelWidth, panelHeight);
            DrawInventoryUI(panelRect, "MI INVENTARIO");

            if (_draggedItem != null)
            {
                Vector2 mousePos = Event.current.mousePosition;
                Rect dragRect = new Rect(mousePos.x - cellSize / 2, mousePos.y - cellSize / 2, cellSize, cellSize);
                if (_draggedItem.itemSprite != null) GUI.DrawTexture(dragRect, _draggedItem.itemSprite.texture);
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
            foreach (var key in _localKeys.ToArray())
            {
                if (!_localBag.TryGetValue(key, out var slot)) continue;
                Rect cell = new Rect(panel.x + padding + (i % columns) * (cellSize + 10),
                                     panel.y + titleH + (i / columns) * (cellSize + 40), cellSize, cellSize);

                bool isOver = cell.Contains(Event.current.mousePosition);
                GUI.DrawTexture(cell, isOver ? _texSelected : _texNormal);
                if (slot.def.itemSprite != null) GUI.DrawTexture(new Rect(cell.x + 10, cell.y + 10, cell.width - 20, cell.height - 20), slot.def.itemSprite.texture);
                GUI.Label(cell, "x" + slot.qty, _qtySty);

                Rect btnArea = new Rect(cell.x, cell.yMax + 2, cell.width, 35);
                int hash = slot.def.GetItemHashCode();
                bool isEquipped = _equippedInstances.ContainsKey(hash);
                string actionText = isEquipped ? "QUIT" : "USE";

                if (slot.def.canUse || slot.def.type == ItemType.Equipment)
                {
                    if (GUI.Button(new Rect(btnArea.x, btnArea.y, btnArea.width * 0.5f, 30), actionText, _btnSty)) UseItem(slot.def);
                }
                if (GUI.Button(new Rect(btnArea.x + (slot.def.canUse || slot.def.type == ItemType.Equipment ? btnArea.width * 0.5f : 0), btnArea.y, slot.def.canUse || slot.def.type == ItemType.Equipment ? btnArea.width * 0.5f : btnArea.width, 30), "DROP", _btnSty)) DropItem(slot.def);

                if (isOver && Event.current.type == EventType.MouseDown && Event.current.button == 0) { _draggedItem = slot.def; Event.current.Use(); }
                i++;
            }
            if (_localKeys.Count == 0) GUI.Label(new Rect(panel.x, panel.y + titleH, panel.width, panel.height - titleH), "Inventario Vacío", _emptySty);
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
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float cx = Mathf.Clamp(x, r, s - 1 - r), cy = Mathf.Clamp(y, r, s - 1 - r);
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > r + 0.5f) px[y * s + x] = Color.clear;
                    else if (bw > 0 && d > r - bw) px[y * s + x] = border;
                    else px[y * s + x] = fill;
                }
            }
            tex.SetPixels(px); tex.Apply(); return tex;
        }

        private static GUIStyle Sty(int sz, FontStyle fs, TextAnchor a, Color c)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = sz, fontStyle = fs, alignment = a };
            s.normal.textColor = c; return s;
        }

        public void UseItem(ItemData item)
        {
            if (item == null) return;

            if (item.type == ItemType.Equipment)
            {
                ToggleEquipment(item);
            }
            else if (item.canUse)
            {
                ApplyConsumableEffect(item);
                int hash = item.GetItemHashCode();
                if (IsNetworkActive) RemoveItemServerRpc(hash, 1);
                else InternalRemoveItem(hash, 1);
            }
        }

        private void ToggleEquipment(ItemData item)
        {
            int hash = item.GetItemHashCode();
            if (_equippedInstances.TryGetValue(hash, out GameObject existing))
            {
                Destroy(existing);
                _equippedInstances.Remove(hash);

                if (IsOwner && item.itemCode.ToLower().Contains("weapon"))
                    EquippedWeaponHash.Value = 0;
            }
            else
            {
                if (item.itemPrefab != null)
                {
                    GameObject instance = Instantiate(item.itemPrefab, transform);
                    _equippedInstances[hash] = instance;

                    // New rule: Only show meshes if the prefab has a CostumeController
                    bool hasVisualModule = instance.GetComponentInChildren<CostumeController>() != null;
                    if (!hasVisualModule)
                    {
                        foreach(var r in instance.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    }

                    if (instance.TryGetComponent<PickupController>(out var p)) DestroyImmediate(p);
                    if (instance.TryGetComponent<Rigidbody>(out var rb)) DestroyImmediate(rb);
                    if (instance.TryGetComponent<NetworkObject>(out var no)) DestroyImmediate(no);

                    // Always disable colliders on equipment to avoid player physics glitches
                    foreach (var c in instance.GetComponentsInChildren<Collider>(true)) c.enabled = false;

                    foreach (var func in instance.GetComponentsInChildren<IItemFunctional>())
                    {
                        func.ApplyEffect(gameObject);
                    }

                    if (IsOwner && item.itemCode.ToLower().Contains("weapon"))
                        EquippedWeaponHash.Value = hash;
                }
            }

            GetComponent<MovementController>()?.RefreshFunctionalComponents();
        }

        private void ApplyConsumableEffect(ItemData item)
        {
            if (item.itemPrefab != null)
            {
                GameObject temp = Instantiate(item.itemPrefab);
                temp.SetActive(false);
                foreach (var func in temp.GetComponentsInChildren<IItemFunctional>())
                {
                    func.ApplyEffect(gameObject);
                }
                Destroy(temp);
            }
        }

        public void DropItem(ItemData item)
        {
            if (item == null) return;
            int hash = item.GetItemHashCode();

            if (_equippedInstances.ContainsKey(hash))
            {
                ToggleEquipment(item);
            }

            Vector3 dropPos = transform.position + transform.right * 1.5f + transform.up * 0.5f;

            if (IsNetworkActive) DropItemServerRpc(hash, dropPos);
            else
            {
                InternalRemoveItem(hash, 1);
                if (_spawnController != null) _spawnController.SpawnDroppedItem(item.itemPrefab, transform.position, item.displayName);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DropItemServerRpc(int hash, Vector3 position)
        {
            ItemData data = GetItemDataByHash(hash);
            if (data != null)
            {
                InternalRemoveItem(hash, 1);
                if (_spawnController != null) _spawnController.SpawnDroppedItem(data.itemPrefab, transform.position, data.displayName);
            }
        }

        public static Dictionary<string, (ItemData def, int qty)> GetBag() => LocalInstance?._localBag ?? new();
        public static void MarkCountDirty() => s_CountDirty = true;
        public static ItemData GetItemDataByCodeStatic(string code) => LocalInstance?.GetItemDataByCode(code);
    }
}
