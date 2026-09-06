using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Combating.Scripts;
using NGO.Networking;

namespace Crafting.Scripts
{
    public class PlayerController : ModularController
    {
        public static PlayerController LocalInstance { get; private set; }

        [Header("Network Data")]
        public NetworkPrefabsList networkPrefabs;
        public List<GameObject> moduleLibrary;

        public NetworkVariable<int> EquippedWeaponHash = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [Header("Panel Settings")]
        public Color accentColor = new Color(1f, 0.85f, 0f, 1f);

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

        private static int s_CollectiblesRemaining = 0;
        private static bool s_CountDirty = true;
        private float _countUpdateTimer = 0f;

        PlayerInput _playerInput;

        protected override void Awake()
        {
            base.Awake();
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                LocalInstance = this;
                InitializeComponents();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsOwner) LocalInstance = this;

            if (IsServer)
            {
                Level.Value = 1;
                Exp.Value = 0;
                ExpToLevelUp.Value = 100f;
                Attack.Value = UnityEngine.Random.Range(12f, 18f);
                Defense.Value = UnityEngine.Random.Range(8f, 12f);

                maxHealth.Value = UnityEngine.Random.Range(110, 136) + (IsOwner ? 15 : 0);
                currentHealth.Value = maxHealth.Value;

                maxFuel.Value = 100f;
                currentFuel.Value = maxFuel.Value;
            }

            if (IsOwner)
            {
                if (LocalUserConfig.UserName != null) playerName.Value = LocalUserConfig.UserName;
                playerColor.Value = LocalUserConfig.UserColor;
            }

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            _playerInput = GetComponent<PlayerInput>();

            if (controller == null) controller = GetComponent<CharacterController>();
            if (controller == null) controller = gameObject.AddComponent<CharacterController>();
            if (controller != null)
            {
                controller.height = 1.8f;
                controller.radius = 0.35f;
                controller.center = new Vector3(0, 0.9f, 0);
                // Increase skinWidth for more stable collision detection and to avoid sinking into geometry
                controller.skinWidth = 0.08f;
                controller.stepOffset = 0.3f;
                controller.slopeLimit = 45f;
            }

            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (mainCamera == null)
            {
                mainCamera = GetComponentInChildren<Camera>(true)?.gameObject;
            }

            RefreshBodyReferences();
            EnsureCoreModules();
            RefreshInputActions();
        }

        private void EnsureCoreModules()
        {
            List<GameObject> allPrefabs = new List<GameObject>();
            if (moduleLibrary != null) allPrefabs.AddRange(moduleLibrary);
            if (networkPrefabs != null)
            {
                foreach (var item in networkPrefabs.PrefabList)
                {
                    if (item.Prefab != null) allPrefabs.Add(item.Prefab);
                }
            }

            if (allPrefabs.Count == 0) return;

            System.Type[] coreComponentTypes = {
                typeof(MovementController), typeof(CameraController), typeof(CursorController),
                typeof(RespawnController), typeof(HudController), typeof(GuiController),
                typeof(LevelingController), typeof(HealthController), typeof(DamageController),
                typeof(HealController),
                typeof(DeathController), typeof(TankController), typeof(SpawnController),
                typeof(SprintController), typeof(SingleJumpController), typeof(DoubleJumpController),
                typeof(GroundController), typeof(LandingController), typeof(MeleeController),
                typeof(ShootController), typeof(InventoryController), typeof(CostumeController),
                typeof(AnimationController)
            };

            foreach (var type in coreComponentTypes)
            {
                // If an existing module of this type is already present in children, ensure it is bound
                var existing = GetComponentInChildren(type, true);
                if (existing != null)
                {
                    foreach (var module in GetComponentsInChildren<IModular>(true))
                    {
                        if (module is MonoBehaviour mb && type.IsAssignableFrom(mb.GetType()))
                        {
                            module.Bind(this);
                        }
                    }

                    continue;
                }

                GameObject prefab = null;
                foreach (var item in allPrefabs)
                {
                    if (item != null && item.GetComponentInChildren(type, true) != null)
                    {
                        prefab = item;
                        break;
                    }
                }

                if (prefab != null)
                {
                    GameObject instance = Instantiate(prefab, transform);
                    instance.name = type.Name;

                    // Apply Hardware Sanitization
                    SanitizeModuleInstance(instance);

                    foreach (var module in instance.GetComponentsInChildren<IModular>(true))
                    {
                        // Ignore deactivated modules if requested
                        if (module is MonoBehaviour mb && !mb.enabled) continue;

                        module.Bind(this);
                    }

                    // Only spawn if it's explicitly allowed and still enabled
                    if (IsServer && IsSpawned && instance.TryGetComponent<NetworkObject>(out var netObj))
                    {
                        if (netObj.enabled) netObj.Spawn(true);
                    }
                }
            }
        }

        public override void RefreshBodyReferences()
        {
            base.RefreshBodyReferences();

            // Player-specific logic for Default Model
            if (activeModel == null && renderRoot != null)
            {
                var defaultPrefab = GetPrefabFromList("DefaultPlayerModel") ?? GetPrefabFromList("ROBOTO FBX ANIMACIONES OK");
                if (defaultPrefab != null)
                {
                    var go = Instantiate(defaultPrefab, renderRoot);
                    go.name = "DefaultModel";
                    go.SetActive(true);
                    activeModel = go.GetComponent<RenderController>() ?? go.AddComponent<RenderController>();
                    base.RefreshBodyReferences(); // Re-run base to sync animator/target
                }
            }
        }

        public override GameObject GetPrefabFromList(string prefabName)
        {
            if (moduleLibrary != null)
            {
                var p = moduleLibrary.FirstOrDefault(x => x != null && x.name == prefabName);
                if (p != null) return p;
            }
            if (networkPrefabs != null)
            {
                var p = networkPrefabs.PrefabList.FirstOrDefault(x => x.Prefab != null && x.Prefab.name == prefabName);
                if (p.Prefab != null) return p.Prefab;
            }
            return null;
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

        private void Update()
        {
            if (!CanExecuteLocalLogic) return;

            UpdateInputState();

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
                if (look.sqrMagnitude < 0.001f && Mouse.current != null)
                {
                    look = Mouse.current.delta.ReadValue() * 0.1f;
                }
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

        public void DrawInventoryUI(Rect rect, string title)
        {
            var items = GetModule<InventoryController>();
            if (items != null) items.DrawInventoryUI(rect, title);
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

        public static Dictionary<string, (ItemData def, int qty)> GetBag() => InventoryController.GetBag();
        public static void MarkCountDirty() => InventoryController.MarkCountDirty();
        public static ItemData GetItemDataByCodeStatic(string code) => InventoryController.LocalInstance != null ? InventoryController.LocalInstance.GetItemDataByCode(code) : null;
        public static void Add(ItemData def) => InventoryController.Add(def);
        public static void RemoveItem(string key) => InventoryController.RemoveItem(key);
    }
}
