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
    public class PlayerController : NetworkBehaviour
    {
        public static PlayerController LocalInstance { get; private set; }

        [Header("Network Data")]
        public List<GameObject> moduleLibrary;

        public NetworkVariable<int> EquippedWeaponHash = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private Dictionary<int, GameObject> _equippedInstances = new();
        private Dictionary<System.Type, MonoBehaviour> _registeredModules = new();

        public T GetModule<T>() where T : MonoBehaviour
        {
            if (_registeredModules.TryGetValue(typeof(T), out var module))
                return module as T;

            // Fallback: búsqueda directa si no está registrado aún
            var found = GetComponentInChildren<T>();
            if (found != null) _registeredModules[typeof(T)] = found;
            return found;
        }

        public void RegisterModule(MonoBehaviour module)
        {
            var type = module.GetType();
            if (!_registeredModules.ContainsKey(type))
            {
                _registeredModules[type] = module;
            }
        }

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

        [Header("Core References")]
        public CharacterController controller;
        public Animator animator;
        public GameObject mainCamera;

        [Header("Hierarchy Articulation")]
        public Transform renderRoot;
        public GameObject cameraTarget;
        public RenderController activeModel;

        // Dynamic properties that always point to the active model's bones
        public Transform HeadPoint => activeModel != null ? activeModel.headPoint : null;
        public Transform SpinePoint => activeModel != null ? activeModel.spinePoint : null;
        public Transform MuzzlePoint => activeModel != null ? activeModel.muzzlePoint : null;
        public Transform CameraLookAtPoint => activeModel != null ? activeModel.cameraLookAtPoint : null;

        private static int s_CollectiblesRemaining = 0;
        private static bool s_CountDirty = true;
        private float _countUpdateTimer = 0f;

        PlayerInput _playerInput;
        SpawnController _spawnController;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;

        private void Awake()
        {
            // En offline, este es siempre el LocalInstance
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                LocalInstance = this;
                InitializeComponents();
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner) LocalInstance = this;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            _playerInput = GetComponent<PlayerInput>();
            _spawnController = GetComponent<SpawnController>();

            if (controller == null) controller = GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.height = 1.8f;
                controller.radius = 0.35f;
                controller.center = new Vector3(0, 0.9f, 0);
                controller.skinWidth = 0.02f;
                controller.stepOffset = 0.3f;
                controller.slopeLimit = 45f;
            }

            if (animator == null) animator = GetComponentInChildren<Animator>();

            // 1. Sanitize Hierarchy positions
            if (cameraTarget == null) cameraTarget = transform.Find("PlayerTarget")?.gameObject;
            if (cameraTarget != null) cameraTarget.transform.localPosition = Vector3.zero;

            // Búsqueda más robusta de la cámara (Tag + Jerarquía)
            if (mainCamera == null)
            {
                var cam = GameObject.FindGameObjectWithTag("MainCamera");
                if (cam == null) cam = GetComponentInChildren<Camera>(true)?.gameObject;
                mainCamera = cam;
            }
            if (mainCamera != null) mainCamera.transform.localPosition = Vector3.zero;

            EnsureCoreModules();
            RefreshInputActions();
            RefreshBodyReferences();
        }

        private void EnsureCoreModules()
        {
            if (moduleLibrary == null || moduleLibrary.Count == 0) return;

            string[] coreModuleNames = {
                "MovementController", "CameraController", "CursorController", "RespawnController",
                "HudController", "UiController", "LevelingController", "HealthController",
                "DamageController", "DeathController", "FuelController", "SpawnController",
                "SprintController", "SingleJumpController", "DoubleJumpController",
                "GroundController", "LandingController", "MeleeController",
                "ShootController", "ItemsController", "ExperienceController", "CostumeController"
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

                    // Bind module if it implements IPlayerModule
                    foreach (var module in instance.GetComponents<IPlayerModule>())
                    {
                        module.Bind(this);
                    }

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
            // 1. Auto-discovery of hierarchy if not set
            if (renderRoot == null) renderRoot = transform.Find("PlayerRender");
            if (cameraTarget == null) cameraTarget = transform.Find("PlayerTarget")?.gameObject;

            // 2. Intelligent Model Discovery (Supporting Variant Prefabs)
            if (renderRoot != null)
            {
                // Check if the root itself or any descendant has the RenderController
                activeModel = renderRoot.GetComponent<RenderController>() ?? renderRoot.GetComponentInChildren<RenderController>(true);

                if (activeModel != null)
                {
                    // Ensure the model is active.
                    // We NO LONGER deactivate siblings to respect Variant Prefab parts.
                    activeModel.gameObject.SetActive(true);
                }
                else
                {
                    // Bootstrapping Visual: If no model found, look for default in library
                    var defaultPrefab = GetPrefabFromList("DefaultPlayerModel") ?? GetPrefabFromList("ROBOTO FBX ANIMACIONES OK");
                    if (defaultPrefab != null)
                    {
                        var go = Instantiate(defaultPrefab, renderRoot);
                        go.name = "DefaultModel";
                        go.SetActive(true);
                        activeModel = go.GetComponent<RenderController>() ?? go.AddComponent<RenderController>();
                    }
                    else
                    {
                        // Final fallback: Make the renderRoot itself the model
                        activeModel = renderRoot.gameObject.AddComponent<RenderController>();
                    }
                }
            }

            // 3. Sync critical components
            if (activeModel != null)
            {
                animator = activeModel.Animator;

                // Ensure all renderers are enabled
                foreach (var r in activeModel.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = true;
                }

                if (cameraTarget != null)
                {
                    // Initial target position sync
                    Transform lookPoint = CameraLookAtPoint ?? HeadPoint ?? activeModel.transform;
                    cameraTarget.transform.position = lookPoint.position;
                }
            }
            else
            {
                animator = GetComponentInChildren<Animator>();
            }

            // 4. Notify all modules
            NotifyModulesRefresh();
        }

        public void NotifyModulesRefresh()
        {
            foreach (var module in _registeredModules.Values)
            {
                if (module is IPlayerModule playerModule)
                {
                    playerModule.OnRefreshModule();
                }
            }
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

                // FALLBACK: Si el look es cero pero el ratón se mueve, forzamos lectura directa
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
            var items = GetModule<ItemsController>();
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

        public static Dictionary<string, (ItemData def, int qty)> GetBag() => ItemsController.GetBag();
        public static void MarkCountDirty() => ItemsController.MarkCountDirty();
        public static ItemData GetItemDataByCodeStatic(string code) => ItemsController.LocalInstance != null ? ItemsController.LocalInstance.GetItemDataByCode(code) : null;
        public static void Add(ItemData def) => ItemsController.Add(def);
        public static void RemoveItem(string key) => ItemsController.RemoveItem(key);
    }
}
