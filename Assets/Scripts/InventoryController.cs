using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Runtime.Scripts.Gate;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Runtime.Scripts.Items;
using Runtime.Scripts.Spawn;
using Runtime.Scripts.Health;
using Runtime.Scripts.Player;
using Runtime.Scripts.Modular;
using Runtime.Scripts.Costume;
using Runtime.Scripts.Jetpack;
using Runtime.Scripts.Weapon;
using Runtime.Scripts.Mask;
using Runtime.Scripts.Pickup;
using Runtime.Scripts.Crafting;
using Runtime.Scripts.Singleton;

namespace Runtime.Scripts.Inventory
{
    public struct NetworkInventorySlot : INetworkSerializable, IEquatable<NetworkInventorySlot>
    {
        public int itemHash;
        public int quantity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref itemHash);
            serializer.SerializeValue(ref quantity);
        }

        public bool Equals(NetworkInventorySlot other) => itemHash == other.itemHash && quantity == other.quantity;
        public override bool Equals(object obj) => obj is NetworkInventorySlot other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(itemHash, quantity);
    }

    [RequireComponent(typeof(SpawnController))]
    public class InventoryController : CSingletonNB<InventoryController>
    {
        public static InventoryController LocalInstance => Instance;

        [Header("Network Data")]
        public NetworkList<NetworkInventorySlot> NetworkBag;

        public NetworkVariable<int> EquippedWeaponHash = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private List<NetworkInventorySlot> _offlineBag = new List<NetworkInventorySlot>();
        private Dictionary<string, (ItemsData def, int qty)> _localBag = new();
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
        [Tooltip("Escala de todo el inventario (se limita para que quepa en pantalla).")] public float uiScale = 2f;
        public int cornerRadius = 15;
        public Color panelColor = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        public Color accentColor = new Color(1f, 0.85f, 0f, 1f);

        [Header("Database & Settings")]
        public List<ItemsData> itemsDatabase;
        public float dropDistance = 3.5f;

        private static int s_CollectiblesRemaining = 0;
        private static bool s_CountDirty = true;
        private float _countUpdateTimer = 0f;

        private bool _open;
        private ItemsData _draggedItem;

        private Texture2D _texNormal, _texSelected, _texPanel, _texBtn;
        private GUIStyle _titleSty, _qtySty, _emptySty, _btnSty;
        private bool _stylesReady;

        PlayerInput _playerInput;
        SpawnController _spawnController;
        HealthController _health;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;

        protected override void Awake()
        {
            base.Awake();
            NetworkBag = new NetworkList<NetworkInventorySlot>();
            InitializeComponents();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            InitializeComponents();
            NetworkBag.OnListChanged += (changeEvent) => RefreshLocalCache();
            RefreshLocalCache();
        }

        private void InitializeComponents()
        {
            _playerInput = GetComponent<PlayerInput>();
            _spawnController = GetComponent<SpawnController>();
            _health = GetComponent<HealthController>();
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
            ItemsData data = GetItemsDataByHash(slot.itemHash);
            if (data != null)
            {
                string key = data.itemName.ToLowerInvariant();
                _localBag[key] = (data, slot.quantity);
                if (!_localKeys.Contains(key)) _localKeys.Add(key);
            }
            else Debug.LogWarning($"[Inventory] El objeto con hash {slot.itemHash} esta en la bolsa pero no se encontro su ItemsData (asset fuera de Resources?)");
        }

        /// <summary>Registra el asset en la base local para que el slot se pueda mostrar aunque no este en Resources.</summary>
        public void RegisterItem(ItemsData d)
        {
            EnsureDatabase();
            if (d != null && !itemsDatabase.Contains(d)) itemsDatabase.Add(d);
        }

        public ItemsData GetItemsDataByHash(int hash)
        {
            EnsureDatabase();
            var found = itemsDatabase.FirstOrDefault(x => x.GetHashCode() == hash);
            if (found == null)
            {
                var allItems = Resources.LoadAll<ItemsData>("");
                found = allItems.FirstOrDefault(x => x.GetHashCode() == hash);
                if (found != null && !itemsDatabase.Contains(found)) itemsDatabase.Add(found);
            }
            return found;
        }

        public ItemsData GetItemsDataByCode(string code)
        {
            EnsureDatabase();
            string c = code.ToLowerInvariant();
            return itemsDatabase.FirstOrDefault(x => x.itemName.ToLowerInvariant() == c);
        }

        private void EnsureDatabase()
        {
            if (itemsDatabase == null || itemsDatabase.Count == 0)
                itemsDatabase = Resources.LoadAll<ItemsData>("").ToList();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AddItemServerRpc(int hash, int qty) => InternalAddItem(hash, qty);

        public void InternalAddItem(int hash, int qty)
        {
            if (IsNetworkActive)
            {
                if (!IsServer)
                {
                    AddItemServerRpc(hash, qty);
                    return;
                }

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
                if (!IsServer)
                {
                    RemoveItemServerRpc(hash, qty);
                    return;
                }

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

        public static void Add(ItemsData def)
        {
            if (LocalInstance == null) return;
            int hash = def.GetHashCode();
            if (LocalInstance.IsNetworkActive) LocalInstance.AddItemServerRpc(hash, 1);
            else LocalInstance.InternalAddItem(hash, 1);
        }

        public static void RemoveItem(string key)
        {
            var data = LocalInstance?.GetItemsDataByCode(key);
            if (data != null)
            {
                int hash = data.GetHashCode();
                if (LocalInstance.IsNetworkActive) LocalInstance.RemoveItemServerRpc(hash, 1);
                else LocalInstance.InternalRemoveItem(hash, 1);
            }
        }

        private void Update()
        {
            if (!CanExecuteLocalLogic) return;
            if (!_panelBuilt) { _panelBuilt = true; CharacterPanel.Prebuild(this); } // se construye una vez, oculto
            if (Keyboard.current != null && (Keyboard.current.iKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame))
                SetOpen(!_open);

            if (s_CountDirty || Time.time > _countUpdateTimer)
            {
                s_CollectiblesRemaining = PickupController.ActiveCount;
                _countUpdateTimer = Time.time + 1.0f;
                s_CountDirty = false;
            }
        }

        // Precarga unica: el marco metalico se genera una sola vez y lo comparten inventario y crafteo (antes se regeneraba en cada evento de OnGUI).
        static Texture2D s_panelTex;
        public static Texture2D PanelTex => s_panelTex != null ? s_panelTex : (s_panelTex = MenuGate.Metal(512, 400));
        static readonly string[] s_qty = new string[1000];
        public static string QtyStr(int q) => q < 0 || q >= 1000 ? "x" + q : (s_qty[q] ?? (s_qty[q] = "x" + q));

        // Cache del tamano de titulo y de la lista de claves: sin CalcSize, sin cambios de fuente y sin ToArray por evento.
        string _fsTitle; float _fsWidth; int _fsVal;
        int _btnFs = -1;

        /// <summary>Slot n-esimo valido de la bolsa (para el panel de personaje).</summary>
        public bool TryGetSlot(int index, out ItemsData def, out int qty, out bool equipped)
        {
            def = null; qty = 0; equipped = false;
            int n = 0;
            for (int k = 0; k < _localKeys.Count; k++)
            {
                if (!_localBag.TryGetValue(_localKeys[k], out var s)) continue;
                if (n++ != index) continue;
                def = s.def; qty = s.qty; equipped = _equippedInstances.ContainsKey(s.def.GetHashCode());
                return true;
            }
            return false;
        }

        /// <summary>Skills que aportan los equipos puestos (mascaras y armas).</summary>
        public void CollectEquipSkills(List<string> names, List<string> keys)
        {
            foreach (var kv in _equippedInstances)
            {
                var go = kv.Value;
                if (go == null) continue;
                var mask = go.GetComponentInChildren<MaskController>(true);
                if (mask != null)
                {
                    var v = mask.EffectiveVisorType;
                    names.Add(v == VisorType.VisorTermico ? "Visor termico" : v == VisorType.VisorEstadisticas ? "Visor de estadisticas" : "Visor de objetos");
                    keys.Add("Mascara");
                }
                else if (go.GetComponentInChildren<WeaponController>(true) != null)
                {
                    var item = GetItemsDataByHash(kv.Key);
                    names.Add("Arma: " + (item != null ? item.itemName : go.name));
                    keys.Add("LMB");
                }
            }
        }

        public static void Toggle() { var i = LocalInstance; if (i != null) i.SetOpen(!i._open); }

        private void OnGate(string k) { if (k == "inventory") SetOpen(false); }

        private void SetOpen(bool open)
        {
            if (open && !MenuGate.TryOpen("inventory")) return; // otro menú abierto
            if (!open && _open) MenuGate.Close("inventory");
            MenuGate.CloseRequested -= OnGate;
            if (open) MenuGate.CloseRequested += OnGate;
            _open = open;
            CharacterPanel.Set(this, open); // panel multiple (inventario + modelo + stats + skills)
            bool free = open || MenuGate.Busy; // no bloquear el cursor si otro menú sigue abierto
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        private bool _panelBuilt;

        private void OnGUI()
        {
            if (!CanExecuteLocalLogic) return;
            if (!_stylesReady) EnsureStyles(); // estilos del inventario embebido en el crafteo
        }

        float K => Mathf.Min(uiScale, (Screen.height - 40f) / panelHeight, (Screen.width - 40f) / panelWidth);

        public void DrawInventoryUI(Rect panel, string title, float scale = 0f)
        {
            EnsureStyles();
            float k = scale > 0f ? scale : K, cs = cellSize * k, pad = padding * k, th = titleH * k, m = 16f * k;
            GUI.DrawTexture(panel, PanelTex);
            GUI.DrawTexture(new Rect(panel.x + m, panel.y + m, panel.width - 2f * m, panel.height - 2f * m), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.01f, 0.03f, 0.02f), 0f, 10f * k);
            if (!ReferenceEquals(_fsTitle, title) || !Mathf.Approximately(_fsWidth, panel.width))
            {
                int fs = (int)(32 * k);
                _titleSty.wordWrap = false;
                _titleSty.fontSize = fs;
                var gc = new GUIContent(title);
                while (fs > 12 && _titleSty.CalcSize(gc).x > panel.width - 130f * k) _titleSty.fontSize = --fs; // el titulo siempre entra en una linea
                _fsTitle = title; _fsWidth = panel.width; _fsVal = _titleSty.fontSize;
            }
            else if (_titleSty.fontSize != _fsVal) _titleSty.fontSize = _fsVal;
            int bf = (int)(10 * k);
            if (bf != _btnFs) { _btnFs = bf; _btnSty.fontSize = bf; }
            GUI.Label(new Rect(panel.x, panel.y + 10f * k, panel.width, th), title, _titleSty);
            if (GUI.Button(new Rect(panel.xMax - 56f * k, panel.y + 22f * k, 35f * k, 35f * k), "X", _btnSty)) SetOpen(false);

            int cols = Mathf.Max(1, (int)((panel.width - 2f * pad) / (cs + 10f * k)));
            int i = 0;
            for (int n = 0; n < _localKeys.Count; n++)
            {
                if (!_localBag.TryGetValue(_localKeys[n], out var slot)) continue;
                Rect cell = new Rect(panel.x + pad + (i % cols) * (cs + 10f * k), panel.y + th + (i / cols) * (cs + 40f * k), cs, cs);

                bool isOver = cell.Contains(Event.current.mousePosition);
                GUI.DrawTexture(cell, isOver ? _texSelected : _texNormal);
                if (slot.def.itemSprite != null) GUI.DrawTexture(new Rect(cell.x + 10f * k, cell.y + 10f * k, cell.width - 20f * k, cell.height - 20f * k), slot.def.itemSprite.texture);
                GUI.Label(cell, QtyStr(slot.qty), _qtySty);

                Rect btnArea = new Rect(cell.x, cell.yMax + 2f * k, cell.width, 35f * k);
                int hash = slot.def.GetHashCode();
                bool isEquipped = _equippedInstances.ContainsKey(hash);
                bool canShowUse = isEquipped ? slot.def.isQuitable : slot.def.isUsable;

                if (canShowUse && GUI.Button(new Rect(btnArea.x, btnArea.y, btnArea.width * 0.5f, 30f * k), isEquipped ? "QUIT" : "USE", _btnSty)) UseItem(slot.def);
                if (slot.def.isDropable)
                {
                    float w = canShowUse ? btnArea.width * 0.5f : btnArea.width, x = canShowUse ? btnArea.x + btnArea.width * 0.5f : btnArea.x;
                    if (GUI.Button(new Rect(x, btnArea.y, w, 30f * k), "DROP", _btnSty)) DropItem(slot.def);
                }

                if (isOver && Event.current.type == EventType.MouseDown && Event.current.button == 0) { _draggedItem = slot.def; Event.current.Use(); }
                i++;
            }
            if (_localKeys.Count == 0) GUI.Label(new Rect(panel.x, panel.y + th, panel.width, panel.height - th), "Inventario Vacío", _emptySty);
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _texNormal = MakeRoundedTex(64, 8, new Color(0.03f, 0.12f, 0.05f), Color.clear, 0);
            _texSelected = MakeRoundedTex(64, 8, new Color(0.05f, 0.2f, 0.08f), MenuGate.Green, 2);
            _texPanel = MenuGate.Metal(350, 275);
            _titleSty = Sty((int)(32 * K), FontStyle.Bold, TextAnchor.MiddleCenter, MenuGate.Green);
            _qtySty = Sty((int)(qtyFontSize * K), FontStyle.Bold, TextAnchor.LowerRight, MenuGate.Green);
            _emptySty = Sty((int)(18 * K), FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.2f, 1f, 0.35f, 0.5f));
            _btnSty = new GUIStyle(GUI.skin.button) { fontSize = (int)(10 * K), fontStyle = FontStyle.Bold };
            _btnSty.normal.textColor = _btnSty.hover.textColor = MenuGate.Green;
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

        public void UseItem(ItemsData item)
        {
            if (item == null) return;
            int hash = item.GetHashCode();
            if (IsNetworkActive) UseItemServerRpc(hash);
            else InternalUseItem(hash);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void UseItemServerRpc(int hash)
        {
            InternalUseItem(hash);
        }

        private void InternalUseItem(int hash)
        {
            ItemsData item = GetItemsDataByHash(hash);
            if (item == null) return;

            bool isEquipped = _equippedInstances.ContainsKey(hash);
            bool isEquipment = isEquipped || (item.itemPrefab != null && (
                item.itemPrefab.GetComponentInChildren<CostumeController>(true) != null ||
                item.itemPrefab.GetComponentInChildren<MaskController>(true) != null ||
                item.itemPrefab.GetComponentInChildren<WeaponController>(true) != null ||
                item.itemPrefab.GetComponentInChildren<JetpackController>(true) != null
            ));

            if (isEquipment)
            {
                ToggleEquipment(item);
            }
            else if (item.isUsable)
            {
                ApplyConsumableEffect(item);
                InternalRemoveItem(hash, 1);
            }
        }

        private void ToggleEquipment(ItemsData item)
        {
            int hash = item.GetHashCode();
            bool isCostume = item.itemPrefab != null && item.itemPrefab.GetComponentInChildren<CostumeController>() != null;
            bool isMask = item.itemPrefab != null && item.itemPrefab.GetComponentInChildren<MaskController>() != null;

            if (_equippedInstances.TryGetValue(hash, out GameObject existing))
            {
                // DESEQUIPAR (QUIT): Ejecutar interfaz dedicada IItemQuitAction
                var quitActions = existing.GetComponentsInChildren<IItemQuitAction>(true);
                foreach (var act in quitActions)
                {
                    act.OnQuitItem(gameObject);
                }

                Destroy(existing);
                _equippedInstances.Remove(hash);
            }
            else
            {
                // EQUIPAR NUEVO:
                if (item.itemPrefab != null)
                {
                    // Si es un Costume, desequipamos cualquier otro costume activo para evitar solapamientos
                    if (isCostume)
                    {
                        var activeCostumes = _equippedInstances.Where(x => x.Value != null && x.Value.GetComponentInChildren<CostumeController>() != null).ToList();
                        foreach (var active in activeCostumes)
                        {
                            var activeQuitActions = active.Value.GetComponentsInChildren<IItemQuitAction>(true);
                            foreach (var act in activeQuitActions) act.OnQuitItem(gameObject);

                            Destroy(active.Value);
                            _equippedInstances.Remove(active.Key);
                        }
                    }

                    // Si es una Máscara/Visor, desequipamos cualquier otra máscara activa
                    if (isMask)
                    {
                        var activeMasks = _equippedInstances.Where(x => x.Value != null && x.Value.GetComponentInChildren<MaskController>() != null).ToList();
                        foreach (var active in activeMasks)
                        {
                            var activeQuitActions = active.Value.GetComponentsInChildren<IItemQuitAction>(true);
                            foreach (var act in activeQuitActions) act.OnQuitItem(gameObject);

                            Destroy(active.Value);
                            _equippedInstances.Remove(active.Key);
                        }
                    }

                    GameObject instance = Instantiate(item.itemPrefab, transform);
                    _equippedInstances[hash] = instance;

                    // Regla visual: Solo ocultamos si es un objeto de pura lógica (sin Costume, Mask ni Weapon Controller)
                    bool hasVisualModule = instance.GetComponentInChildren<CostumeController>() != null ||
                                         instance.GetComponentInChildren<MaskController>() != null ||
                                         instance.GetComponentInChildren<WeaponController>() != null;

                    if (!hasVisualModule)
                    {
                        // Si no tiene controladores visuales, asumimos que es un booster invisible
                        foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    }

                    // Quitar componentes de mundo inmediatos antes de reemparentar
                    if (instance.TryGetComponent<PickupController>(out var p)) DestroyImmediate(p);
                    if (instance.TryGetComponent<Rigidbody>(out var rb)) DestroyImmediate(rb);

                    var netObjs = instance.GetComponentsInChildren<NetworkObject>(true);
                    foreach (var no in netObjs) DestroyImmediate(no);

                    // Desactivar colisionadores para evitar que el player salga volando
                    foreach (var c in instance.GetComponentsInChildren<Collider>(true)) c.enabled = false;

                    // Aplicar acciones dedicadas IItemUseAction o fallback a IItemFunctional
                    var useActions = instance.GetComponentsInChildren<IItemUseAction>(true);
                    if (useActions.Length > 0)
                    {
                        foreach (var act in useActions) act.OnUseItem(gameObject);
                    }
                    else
                    {
                    }
                }
            }

            // Refrescar referencias del robot (muzzle, cámara, etc)
            GetComponent<PlayerController>()?.RefreshFunctionalComponents();
            GetComponent<ModularController>()?.RefreshBodyReferences();
        }

        private void ApplyConsumableEffect(ItemsData item)
        {
            if (item.itemPrefab != null)
            {
                var useActions = item.itemPrefab.GetComponentsInChildren<IItemUseAction>(true);
                if (useActions.Length > 0)
                {
                    foreach (var act in useActions) act.OnUseItem(gameObject);
                    return;
                }

                Debug.LogWarning($"[Inventory] El ítem {item.itemName} no tiene componentes de efecto válidos.");
            }
        }

        public void DropItem(ItemsData item)
        {
            if (item == null) return;
            int hash = item.GetHashCode();

            if (_equippedInstances.TryGetValue(hash, out GameObject equippedObj))
            {
                // Ejecutar acciones DROP dedicadas si las tiene antes de desequipar
                var dropActions = equippedObj.GetComponentsInChildren<IItemDropAction>(true);
                foreach (var act in dropActions)
                {
                    act.OnDropItem(gameObject);
                }

                ToggleEquipment(item);
            }

            // Usar dropDistance para el desplazamiento lateral y asegurar altura
            Vector3 dropPos = transform.position + transform.right * dropDistance + transform.up * 1.8f;

            if (IsNetworkActive) DropItemServerRpc(hash, dropPos);
            else
            {
                InternalRemoveItem(hash, 1);
                if (_spawnController != null) _spawnController.SpawnDroppedItem(item.itemPrefab, dropPos, item.itemName, item);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DropItemServerRpc(int hash, Vector3 position)
        {
            ItemsData data = GetItemsDataByHash(hash);
            if (data != null)
            {
                InternalRemoveItem(hash, 1);
                if (_spawnController != null) _spawnController.SpawnDroppedItem(data.itemPrefab, position, data.itemName, data);
            }
        }

        public static Dictionary<string, (ItemsData def, int qty)> GetBag() => LocalInstance?._localBag ?? new();
        public Dictionary<string, (ItemsData def, int qty)> GetMyBag() => _localBag;
        public static void MarkCountDirty() => s_CountDirty = true;
        public static ItemsData GetItemsDataByCodeStatic(string code) => LocalInstance?.GetItemsDataByCode(code);
    }
}