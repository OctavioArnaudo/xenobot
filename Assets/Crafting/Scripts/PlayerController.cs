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

    public class PlayerController : NetworkBehaviour
    {
        public static PlayerController LocalInstance { get; private set; }

        [Header("Network Data")]
        public NetworkList<NetworkInventorySlot> NetworkBag;
        public List<GameObject> moduleLibrary;

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

        [Header("Shared Input Data")]
        public Vector2 move;
        public Vector2 look;
        public bool jump;
        public bool jumpHeld;
        public bool sprint;
        public bool fire;
        public bool fireHeld;
        public bool fireReleased;
        public bool aim;
        public bool crouch;
        public bool reload;
        public int switchWeapon;
        public int selectWeapon;
        public bool analogMovement;
        public bool cursorLocked = true;
        public bool cursorInputForLook = true;

        [Header("Core References")]
        public CharacterController controller;
        public Animator animator;
        public GameObject mainCamera;

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

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;

        private void Awake()
        {
            LocalInstance = this; // Forzamos la instancia local inmediatamente
            NetworkBag = new NetworkList<NetworkInventorySlot>();
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
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

            if (controller == null) controller = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // Búsqueda más robusta de la cámara
            if (mainCamera == null)
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null) mainCamera = cam.gameObject;
                else mainCamera = Camera.main != null ? Camera.main.gameObject : null;
            }

            EnsureCoreModules();
            RefreshInputActions();
        }

        private void EnsureCoreModules()
        {
            if (moduleLibrary == null || moduleLibrary.Count == 0) return;

            string[] coreModuleNames = {
                "MovementController", "CameraController", "CursorController", "RespawnController",
                "HudController", "UiController", "LevelingController", "HealthController",
                "DamageController", "DeathController", "FuelController", "SpawnController",
                "SprintController", "SingleJumpController", "DoubleJumpController",
                "GroundController", "LandingController", "MeleeController"
            };

            foreach (var moduleName in coreModuleNames)
            {
                // Check if the component or a child with that name already exists
                if (GetComponent(moduleName) != null || transform.Find(moduleName) != null) continue;

                var prefab = moduleLibrary.FirstOrDefault(x => x != null && x.name == moduleName);
                if (prefab != null)
                {
                    GameObject instance = Instantiate(prefab, transform);
                    instance.name = moduleName;

                    if (IsServer && IsSpawned && instance.TryGetComponent<NetworkObject>(out var netObj))
                    {
                        netObj.Spawn(true);
                    }
                }
            }
        }

        public GameObject GetPrefabFromList(string prefabName)
        {
            if (moduleLibrary == null) return null;
            return moduleLibrary.FirstOrDefault(x => x != null && x.name == prefabName);
        }

        public void RefreshBodyReferences()
        {
            animator = GetComponentInChildren<Animator>();

            // Notify other modular scripts to refresh their references
            var moveScript = GetComponent<MovementController>();
            if (moveScript != null) moveScript.RefreshFunctionalComponents();

            var camScript = GetComponent<CameraController>();
            if (camScript != null) camScript.RefreshFunctionalComponents();
        }

        private InputAction _moveAction, _lookAction, _jumpAction, _fireAction, _sprintAction, _aimAction, _crouchAction, _reloadAction, _nextWeaponAction;

        private void RefreshInputActions()
        {
            if (_playerInput == null || _playerInput.actions == null) return;
            var actions = _playerInput.actions;
            _moveAction = actions.FindAction("Move") ?? actions.FindAction("Player/Move");
            _lookAction = actions.FindAction("Look") ?? actions.FindAction("Player/Look");
            _jumpAction = actions.FindAction("Jump") ?? actions.FindAction("Player/Jump");
            _fireAction = actions.FindAction("Fire") ?? actions.FindAction("Player/Fire");
            _sprintAction = actions.FindAction("Sprint") ?? actions.FindAction("Player/Sprint");
            _aimAction = actions.FindAction("Aim") ?? actions.FindAction("Player/Aim");
            _crouchAction = actions.FindAction("Crouch") ?? actions.FindAction("Player/Crouch");
            _reloadAction = actions.FindAction("Reload") ?? actions.FindAction("Player/Reload");
            _nextWeaponAction = actions.FindAction("NextWeapon") ?? actions.FindAction("Player/NextWeapon");
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

            UpdateInputState();

            if (Keyboard.current != null && (Keyboard.current.iKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame))
                SetOpen(!_open);

            if (s_CountDirty || Time.time > _countUpdateTimer)
            {
                s_CollectiblesRemaining = PickupController.ActiveCount;
                _countUpdateTimer = Time.time + 1.0f;
                s_CountDirty = false;
            }
        }

        private void UpdateInputState()
        {
            if (_moveAction != null) move = _moveAction.ReadValue<Vector2>();

            if (_lookAction != null)
            {
                look = _lookAction.ReadValue<Vector2>();
            }
            if (_jumpAction != null)
            {
                if (_jumpAction.WasPressedThisFrame()) jump = true;
                jumpHeld = _jumpAction.IsPressed();
            }

            sprint = _sprintAction != null && _sprintAction.IsPressed();

            if (_fireAction != null)
            {
                if (_fireAction.WasPressedThisFrame()) fire = true;
                fireHeld = _fireAction.IsPressed();
                if (_fireAction.WasReleasedThisFrame()) fireReleased = true;
            }

            aim = _aimAction != null && _aimAction.IsPressed();
            if (_crouchAction != null && _crouchAction.WasPressedThisFrame()) crouch = true;
            if (_reloadAction != null && _reloadAction.WasPressedThisFrame()) reload = true;

            if (_nextWeaponAction != null)
            {
                float val = _nextWeaponAction.ReadValue<float>();
                switchWeapon = val > 0 ? 1 : (val < 0 ? -1 : 0);
            }

            selectWeapon = 0;
            if (Keyboard.current != null)
            {
                for (int i = 1; i <= 9; i++)
                {
                    if (Keyboard.current[Key.Digit1 + (i - 1)].wasPressedThisFrame)
                    {
                        selectWeapon = i;
                        break;
                    }
                }
            }
        }

        private void SetOpen(bool open)
        {
            _open = open;
            // No deshabilitamos el PlayerInput por completo, ya que gestiona otras cosas
            // Solo controlamos el estado del cursor
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;

            // Si el inventario está abierto, forzamos inputs a cero
            if (open)
            {
                move = Vector2.zero;
                look = Vector2.zero;
            }
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
                GameObject prefab = item.itemPrefab;
                if (prefab == null) prefab = GetPrefabFromList(item.itemCode);

                if (prefab != null)
                {
                    GameObject instance = Instantiate(prefab, transform);
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

        public void RequestFire(ProjectileController prefab, Vector3 direction, Vector3 spawnPos, float damage, Team team)
        {
            if (IsNetworkActive)
            {
                FireServerRpc(direction, spawnPos, damage, team);
            }
            else
            {
                ProjectileController projectile = Instantiate(prefab, spawnPos, Quaternion.LookRotation(direction));
                if (projectile != null) projectile.Launch(gameObject, direction, damage, team);
            }
        }

        [Rpc(SendTo.Server)]
        private void FireServerRpc(Vector3 direction, Vector3 spawnPos, float damage, Team team)
        {
            ProjectileController projectilePrefab = null;
            var shooter = GetComponentInChildren<ShootController>();
            if (shooter != null) projectilePrefab = shooter.ProjectilePrefab;

            if (projectilePrefab != null)
            {
                ProjectileController instance = Instantiate(projectilePrefab, spawnPos, Quaternion.LookRotation(direction));
                instance.Launch(gameObject, direction, damage, team);
                instance.GetComponent<NetworkObject>().Spawn();
            }
        }

        public static Dictionary<string, (ItemData def, int qty)> GetBag() => LocalInstance?._localBag ?? new();
        public static void MarkCountDirty() => s_CountDirty = true;
        public static ItemData GetItemDataByCodeStatic(string code) => LocalInstance?.GetItemDataByCode(code);
    }
}
