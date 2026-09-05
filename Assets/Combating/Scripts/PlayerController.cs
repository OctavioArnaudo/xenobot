using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Combating.Scripts;

namespace Crafting.Scripts
{
    /// <summary>
    /// Interfaz base para todos los módulos que forman parte del ecosistema del Jugador.
    /// Permite una vinculación (Binding) explícita con el Hub central.
    /// </summary>
    public interface IPlayer
    {
        /// <summary>
        /// Vincula el módulo con el PlayerController central.
        /// Se llama inmediatamente después de la instanciación o en el Awake/OnNetworkSpawn.
        /// </summary>
        void Bind(PlayerController hub);

        /// <summary>
        /// Se llama cuando el Hub detecta un cambio importante en la jerarquía
        /// (ej: cambio de traje, cambio de animator).
        /// </summary>
        void OnRefreshModule();
    }
    public class PlayerController : NetworkBehaviour
    {
        public static PlayerController LocalInstance { get; private set; }

        [Header("Network Data")]
        public NetworkPrefabsList networkPrefabs;
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
        // Usamos EnsurePoints para evitar que la cámara apunte al 0,0,0 por falta de inicialización
        public Transform HeadPoint { get { if (activeModel != null) activeModel.EnsurePoints(); return activeModel != null ? activeModel.headPoint : null; } }
        public Transform SpinePoint { get { if (activeModel != null) activeModel.EnsurePoints(); return activeModel != null ? activeModel.spinePoint : null; } }
        public Transform MuzzlePoint { get { if (activeModel != null) activeModel.EnsurePoints(); return activeModel != null ? activeModel.muzzlePoint : null; } }
        public Transform CameraLookAtPoint { get { if (activeModel != null) activeModel.EnsurePoints(); return activeModel != null ? activeModel.cameraLookAtPoint : null; } }

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
            // Los módulos autónomos se encargan de su propia jerarquía
            if (renderRoot == null) renderRoot = transform.Find("PlayerRender");

            // 2. Búsqueda de la cámara
            if (mainCamera == null)
            {
                mainCamera = GetComponentInChildren<Camera>(true)?.gameObject;
            }

            // IMPORTANTE: Primero descubrimos el cuerpo y sus huesos, LUEGO creamos los módulos
            RefreshBodyReferences();
            EnsureCoreModules();
            RefreshInputActions();
        }

        private void EnsureCoreModules()
        {
            // Combinar fuentes de prefabs: library manual + asset de red
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

            // Tipos de componentes que buscamos instanciar
            System.Type[] coreComponentTypes = {
                typeof(MovementController), typeof(CameraController), typeof(CursorController),
                typeof(RespawnController), typeof(HudController), typeof(UiController),
                typeof(LevelingController), typeof(HealthController), typeof(DamageController),
                typeof(DeathController), typeof(FuelController), typeof(SpawnController),
                typeof(SprintController), typeof(SingleJumpController), typeof(DoubleJumpController),
                typeof(GroundController), typeof(LandingController), typeof(MeleeController),
                typeof(ShootController), typeof(ItemsController), typeof(CostumeController)
            };

            foreach (var type in coreComponentTypes)
            {
                // 1. Verificamos si ya existe el componente en el Player o sus hijos
                if (GetComponentInChildren(type, true) != null) continue;

                // 2. Buscamos en la librería un prefab que tenga ese componente
                GameObject prefab = null;
                foreach (var item in allPrefabs)
                {
                    if (item != null && item.GetComponentInChildren(type, true) != null)
                    {
                        prefab = item;
                        break;
                    }
                }

                // 3. Si lo encontramos, lo instanciamos
                if (prefab != null)
                {
                    GameObject instance = Instantiate(prefab, transform);
                    // Le ponemos el nombre del tipo para mantener orden
                    instance.name = type.Name;

                    // Vinculamos usando la nueva interfaz IPlayer
                    foreach (var module in instance.GetComponentsInChildren<IPlayer>(true))
                    {
                        module.Bind(this);
                    }

                    if (IsServer && IsSpawned && instance.TryGetComponent<NetworkObject>(out var netObj))
                    {
                        netObj.Spawn(true);
                    }
                }
                else
                {
                    Debug.LogWarning($"[PlayerController] No se encontró un prefab en la moduleLibrary para el sistema: {type.Name}");
                }
            }
        }

        public GameObject GetPrefabFromList(string prefabName)
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

        public void RefreshBodyReferences()
        {
            // 1. Auto-discovery of hierarchy if not set
            if (renderRoot == null) renderRoot = transform.Find("PlayerRender");
            if (cameraTarget == null) cameraTarget = transform.Find("PlayerTarget")?.gameObject;

            // 2. Intelligent Model Discovery (Supporting Variant Prefabs)
            if (renderRoot != null)
            {
                // Priorizamos los hijos (Variant Prefabs) sobre el contenedor root para que los puntos de interés específicos manden
                activeModel = renderRoot.GetComponentsInChildren<RenderController>(true)
                    .FirstOrDefault(rc => rc.transform != renderRoot)
                    ?? renderRoot.GetComponent<RenderController>();

                if (activeModel != null)
                {
                    // Ensure the model is active.
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

                // Forzar al Animator a reiniciarse con el nuevo Avatar
                if (animator != null)
                {
                    animator.enabled = true;
                    animator.Rebind();
                    animator.Update(0);
                }

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
                if (module is IPlayer playerModule)
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