using UnityEngine;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Unity.Netcode;
using Unity.Cinemachine;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Effective;
using Runtime.Scripts.Health;
using Runtime.Scripts.Shoot;
using Runtime.Scripts.Inventory;
using Runtime.Scripts.Propulsion;
using Runtime.Scripts.Melee;
using Runtime.Scripts.Singleton;
using Runtime.Scripts.Projectile;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Hook;
using Runtime.Scripts.Defeat;
using Runtime.Scripts.Keys;
using Runtime.Scripts.Zoom;
using Runtime.Scripts.Gate;
using Runtime.Scripts.Shield;

namespace Runtime.Scripts.Player
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class PlayerController : CSingletonNB<PlayerController>
    {
        private const float DEFAULT_MOVE_SPEED = 2.0f;
        private const float DEFAULT_SPRINT_SPEED = 5.335f;
        private const float DEFAULT_SPRINT_HOLD_THRESHOLD = 0.25f;
        private const float DEFAULT_ROTATION_SMOOTH_TIME = 0.12f;
        private const float DEFAULT_SPEED_CHANGE_RATE = 10.0f;
        private const float DEFAULT_JUMP_HEIGHT = 1.2f;
        private const float DEFAULT_DOUBLE_JUMP_HEIGHT = 1.2f;
        private const int DEFAULT_MAX_JUMPS = 2;
        private const float DEFAULT_GRAVITY = -15.0f;
        private const float DEFAULT_GROUNDED_RADIUS = 0.15f;
        private const float DEFAULT_GROUNDED_OFFSET = -0.14f;

        #region Variables: Movement Settings
        [Header("Movement")]
        public float MoveSpeed;
        public Optional<float> moveSpeed;
        public float SprintSpeed;
        public Optional<float> sprintSpeed;
        public float SprintHoldThreshold;
        public Optional<float> sprintHoldThreshold;
        [Range(0.0f, 0.3f)] public float RotationSmoothTime;
        public Optional<float> rotationSmoothTime;
        public float SpeedChangeRate;
        public Optional<float> speedChangeRate;
        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        [Header("Jump Physics & Overrides")]
        public float JumpHeight;
        public Optional<float> jumpHeight;
        public bool EnableDoubleJump = true;
        public float DoubleJumpHeight;
        public Optional<float> doubleJumpHeight;
        public int MaxJumps;
        public Optional<int> maxJumps;

        public float Gravity;
        public Optional<float> gravity;
        public float JumpTimeout = 0.50f;
        public float FallTimeout = 0.15f;
        [Tooltip("Gravedad extra al caer")] public float FallGravityMultiplier = 2f;
        #endregion

        #region Variables: Aiming & Combat UI
        [Header("Aiming & Combat Mode")]
        public GameObject crosshairUI;
        public float aimFOV = 40f;
        public float normalFOV = 60f;
        public float aimCameraDistance = 0.0f;
        public float defaultCameraDistance = 5.2f;
        public Vector3 aimShoulderOffset = new Vector3(0.55f, 0.05f, 0.8f);
        public Vector3 defaultShoulderOffset = new Vector3(0.35f, 0.0f, 0.0f);
        public bool toggleAim = false;

        [Header("Camara de hombro (clic derecho)")]
        public Vector3 shoulderOffset = new Vector3(0.9f, 0.3f, 0f);
        public float shoulderDistance = 3.8f;
        public float cameraRise = 0.6f;
        public Vector3 shoulderAimOffset = new Vector3(1.0f, 0.25f, 0f);
        public float shoulderAimDistance = 2.2f;
        public LayerMask cameraCollision = ~0;
        public float shoulderAimFOV = 50f;
        [Range(0f, 1f)] public float aimZoomStrength = 0.3f;
        public float aimZoomSpeed = 5f;
        [Range(0.2f, 1f)] public float aimMoveSpeedMultiplier = 0.6f;
        [Tooltip("True = Modo Melee (1), False = Modo Rango (2).")] public bool meleeMode;
        public bool scopeMode;
        public bool HasMelee => _melee != null;
        public bool HasHook => hookProjectilePrefab != null;

        [Header("Hook Settings")]
        public GameObject hookProjectilePrefab;
        public Transform hookMuzzle;
        #endregion

        #region Variables: Ground Check
        [Header("Ground Check")]
        public bool Grounded = true;
        public float GroundedOffset;
        public Optional<float> groundedOffset;
        public float GroundedRadius;
        public Optional<float> groundedRadius;
        public LayerMask GroundLayers;
        #endregion

        #region Variables: Camera & Look
        [Header("Cinemachine")]
        public GameObject CinemachineCameraTarget;
        public string PlayerVCamTag = "PlayerVCam";
        public float TopClamp = 70.0f;
        public float BottomClamp = -30.0f;
        public float CameraAngleOverride = 0.0f;
        public bool LockCameraPosition = false;
        public Vector2 LookSensitivity = new Vector2(7.5f, 5.0f);
        #endregion

        #region Variables: Respawn
        [System.Serializable]
        public struct SceneSpawnConfig
        {
            public string SceneName;
            public string SpawnPointTag;
        }

        [Header("Respawn & Spawning")]
        public List<SceneSpawnConfig> SceneSpawns = new List<SceneSpawnConfig>();
        public float yThreshold = -5f;
        public AudioClip respawnSound;
        private Vector3 _startingPosition;
        private Quaternion _startingRotation;
        #endregion

        #region Variables: Input Data
        [Header("Input Data")]
        public Vector2 move;
        public Vector2 look;
        public bool jump;
        public bool jumpHeld;
        public bool sprint;
        public bool fire;
        public bool fireHeld;
        public bool fireReleased;
        public bool aim;
        public bool scope;
        public bool hook;
        public bool crouch;
        public bool reload;
        public int switchWeapon;
        public int selectWeapon;
        public bool analogMovement;
        public bool cursorLocked = true;
        public bool cursorInputForLook = true;
        #endregion

        #region Effective Statistics Resolvers
        public float EffectiveMoveSpeed
        {
            get
            {
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return moveSpeed.Get(MoveSpeed, DEFAULT_MOVE_SPEED, stats.moveSpeed);
            }
        }

        public float EffectiveSprintSpeed => sprintSpeed.Get(SprintSpeed, DEFAULT_SPRINT_SPEED);
        public float EffectiveSprintHoldThreshold => sprintHoldThreshold.Get(SprintHoldThreshold, DEFAULT_SPRINT_HOLD_THRESHOLD);
        public float EffectiveRotationSmoothTime => rotationSmoothTime.Get(RotationSmoothTime, DEFAULT_ROTATION_SMOOTH_TIME);
        public float EffectiveSpeedChangeRate => speedChangeRate.Get(SpeedChangeRate, DEFAULT_SPEED_CHANGE_RATE);

        public float EffectiveJumpHeight
        {
            get
            {
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return jumpHeight.Get(JumpHeight, DEFAULT_JUMP_HEIGHT, stats.jumpHeight);
            }
        }

        public float EffectiveDoubleJumpHeight => doubleJumpHeight.Get(DoubleJumpHeight, DEFAULT_DOUBLE_JUMP_HEIGHT);

        public int EffectiveMaxJumps
        {
            get
            {
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return maxJumps.Get(MaxJumps, DEFAULT_MAX_JUMPS, stats.maxJumps);
            }
        }

        public float EffectiveGravity => gravity.Get(Gravity, DEFAULT_GRAVITY);
        public float EffectiveGroundedRadius => groundedRadius.Get(GroundedRadius, DEFAULT_GROUNDED_RADIUS);
        public float EffectiveGroundedOffset => groundedOffset.Get(GroundedOffset, DEFAULT_GROUNDED_OFFSET);
        #endregion

        #region Internal State
        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;
        private Vector3 _cameraStartingPosition;
        private Quaternion _cameraStartingRotation;
        public bool IsRespawning { get; set; } = false;

        private float _speed;
        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private Vector3 _moveDir = Vector3.forward;
        private bool _gameCursor;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private Vector3 _kb;
        private float _terminalVelocity = 53.0f;

        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;
        private int _jumpsRemaining;

        private float _shiftHoldTimer;

        private static readonly int _animIDSpeed = Animator.StringToHash("Speed");
        private static readonly int _animIDIsGrounded = Animator.StringToHash("isGrounded");
        private static readonly int _animIDJump = Animator.StringToHash("Jump");

        private bool _hasAnimIDSpeed;
        private bool _hasAnimIDIsGrounded;
        private bool _hasAnimIDJump;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
        private Animator _animator;
        private Transform _renderTransform;
        private CharacterController _controller;
        private GameObject _mainCamera;
        private Unity.Cinemachine.CinemachineCamera _vcamCache;
        private Unity.Cinemachine.CinemachineThirdPersonFollow _vcam3rdPerson;

        private HealthController _health;
        private PropulsionController _jetpack;
        private InventoryController _inventory;
        private ShootController _shooter;
        private MeleeController _melee;
        private ShieldController _shield;

        private bool _isJumpHeld;
        private const float _threshold = 0.01f;
        private bool _hasAnimator;

        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _fireAction;
        private InputAction _sprintAction;
        private InputAction _aimAction;
        private InputAction _hookAction;
        private InputAction _crouchAction;
        private InputAction _reloadAction;
        private InputAction _nextWeaponAction;

        private bool IsCurrentDeviceMouse => _playerInput != null && _playerInput.currentControlScheme == "KeyboardMouse";
        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;
        #endregion

        #region Lifecycle
        protected override void Awake()
        {
            base.Awake();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                var nObj = GetComponent<NetworkObject>();
                if (nObj == null || nObj.InScenePlaced == true)
                {
                    gameObject.SetActive(false);
                    Destroy(gameObject);
                    return;
                }
            }

            _controller = GetComponent<CharacterController>();
            _health = GetComponent<HealthController>();
            if (_health != null)
            {
                _health.OnDeath.AddListener(OnPlayerDeath);
            }
            _inventory = GetComponent<InventoryController>();

            EnableDoubleJump = true;
            if (JumpHeight < 1f) JumpHeight = 1.2f;
            if (DoubleJumpHeight < 1f) DoubleJumpHeight = 1.2f;

            RefreshFunctionalComponents();

#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
            RefreshInputActions();
#endif

            if (_mainCamera == null)
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");

            if (CanExecuteLocalLogic)
            {
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "BiomaScene")
                    SetCursorState(cursorLocked);
                else
                    SetCursorState(false);
            }
        }

        private void Start()
        {
            _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;
            _animator = GetComponentInChildren<Animator>();
            _hasAnimator = _animator != null;

            AssignAnimationIDs();

            _cameraStartingPosition = CinemachineCameraTarget.transform.position;
            _cameraStartingRotation = CinemachineCameraTarget.transform.rotation;
            _startingPosition = transform.position;
            _startingRotation = transform.rotation;
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;

            if (!IsNetworkActive)
            {
                SetupPlayerLocal();
                TeleportToSceneSpawn(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }
        }

        public override void OnNetworkSpawn()
        {
            if (BalanceManager.Instance != null)
            {
                BalanceManager.Instance.RegisterNetworkPlayer(gameObject);
            }

            if (IsOwner)
            {
                GameObject[] playersInScene = GameObject.FindGameObjectsWithTag("Player");
                foreach (var p in playersInScene)
                {
                    if (p == gameObject) continue;

                    NetworkObject nObj = p.GetComponent<NetworkObject>();

                    if (nObj == null || nObj.InScenePlaced == true)
                    {
                        foreach (var cam in p.GetComponentsInChildren<Camera>()) cam.enabled = false;
                        var vcam = p.GetComponentInChildren<Unity.Cinemachine.CinemachineCamera>();
                        if (vcam != null) vcam.enabled = false;

                        Destroy(p);
                    }
                }

                NetworkObject myNetObj = GetComponentInParent<NetworkObject>() ?? GetComponent<NetworkObject>();
                if (myNetObj != null) myNetObj.transform.SetParent(null);

                SetupPlayerLocal();
                TeleportToSceneSpawn(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

                var listener = GetComponentInChildren<AudioListener>();
                if (listener != null) listener.enabled = true;

                var renderers = GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers) r.enabled = true;

                UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            }
            else
            {
                DisableLocalComponents();
                if (_controller != null) _controller.enabled = false;
                var vcam = GetComponentInChildren<Unity.Cinemachine.CinemachineCamera>();
                if (vcam != null) vcam.enabled = false;
                var listener = GetComponentInChildren<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
                UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (IsOwner || !IsNetworkActive)
            {
                _gameCursor = false;
                SetupCamera();
                TeleportToSceneSpawn(scene.name);
#if ENABLE_INPUT_SYSTEM
                if (_playerInput != null) _playerInput.ActivateInput();
#endif
            }
        }

        private void TeleportToSceneSpawn(string sceneName)
        {
            var config = SceneSpawns.Find(s => s.SceneName == sceneName);
            if (string.IsNullOrEmpty(config.SpawnPointTag)) return;

            GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag(config.SpawnPointTag);
            if (spawnPoints.Length > 0)
            {
                int index = (int)(OwnerClientId % (ulong)spawnPoints.Length);
                Transform targetSpawn = spawnPoints[index].transform;
                if (_controller != null) _controller.enabled = false;
                transform.position = targetSpawn.position;
                transform.rotation = targetSpawn.rotation;
                _startingPosition = transform.position;
                _startingRotation = transform.rotation;
                if (_controller != null) _controller.enabled = true;
            }
        }

        public override void OnDestroy()
        {
            if (_health != null)
            {
                _health.OnDeath.RemoveListener(OnPlayerDeath);
            }
            base.OnDestroy();
        }

        private bool _isDead = false;

        private void OnPlayerDeath()
        {
            if (_isDead) return;
            _isDead = true;

            SpawnDeathVisuals();

            var defeatMenu = FindFirstObjectByType<DefeatMenu>();
            if (defeatMenu != null)
            {
                defeatMenu.TriggerDefeat();
            }

            if (IsNetworkActive && IsServer)
            {
                if (TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
                    netObj.Despawn(true);
                else
                    Destroy(gameObject);
            }
            else if (!IsNetworkActive)
            {
                Destroy(gameObject);
            }
        }

        private void SpawnDeathVisuals()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = transform.position + Vector3.up * 1.0f;
            sphere.transform.localScale = Vector3.one * 1.2f;
            if (sphere.TryGetComponent<Collider>(out var c)) Destroy(c);

            var mr = sphere.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            mat.color = Color.red;
            mr.material = mat;
            Destroy(sphere, 0.25f);
        }

        private void Update()
        {
            if (!CanExecuteLocalLogic)
            {
                if (_renderTransform != null)
                {
                    _renderTransform.localPosition = new Vector3(0, -0.05f, 0);
                    _renderTransform.localRotation = Quaternion.identity;
                }
                return;
            }

            if (_health != null && _health.EffectiveCurrentHealth <= 0)
            {
                OnPlayerDeath();
                return;
            }

#if ENABLE_INPUT_SYSTEM
            if (_fireAction == null) RefreshInputActions();
            UpdateInputState();
            if (Cursor.lockState == CursorLockMode.Locked) _gameCursor = true;
            if (_gameCursor && Cursor.visible) ClearInput();
#endif

            HandleAimingAndCombat();
            GroundedCheck();
            HandleRespawn();
            JumpAndGravity();
            Move();
            UpdateAnimatorParameters();
        }

        private void LateUpdate()
        {
            if (!CanExecuteLocalLogic) return;
            UpdateCameraTargetPosition();
            CameraRotation();
        }

        private void UpdateCameraTargetPosition()
        {
            if (CinemachineCameraTarget == null) return;

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null)
            {
                Transform bone = anim.GetBoneTransform(HumanBodyBones.UpperChest) ??
                               anim.GetBoneTransform(HumanBodyBones.Chest) ??
                               anim.GetBoneTransform(HumanBodyBones.Neck) ??
                               anim.GetBoneTransform(HumanBodyBones.Head) ??
                               anim.transform.Find("spine") ??
                               anim.transform.Find("Chest") ??
                               anim.transform.Find("head");

                if (bone != null)
                {
                    CinemachineCameraTarget.transform.position = bone.position + Vector3.up * 0.1f;
                }
            }
        }
        #endregion

        #region Setup & Configuration
        private void SetupPlayerLocal()
        {
#if ENABLE_INPUT_SYSTEM
            if (_playerInput != null)
            {
                _playerInput.enabled = true;
                _playerInput.ActivateInput();
            }
#endif
            SetupCamera();
            SetCursorState(cursorLocked);
        }

        private void DisableLocalComponents()
        {
#if ENABLE_INPUT_SYSTEM
            if (_playerInput != null)
            {
                _playerInput.DeactivateInput();
                _playerInput.enabled = false;
            }
#endif
        }

        private Unity.Cinemachine.CinemachineThirdPersonFollow Tpf()
        {
            var f = _vcamCache.GetComponent<Unity.Cinemachine.CinemachineThirdPersonFollow>();
            if (f == null) return null;
            var o = f.AvoidObstacles;
            o.Enabled = true;
            o.CollisionFilter = cameraCollision;
            o.CameraRadius = 0.3f;
            o.IgnoreTag = "Player";
            f.AvoidObstacles = o;
            return f;
        }

        private void SetupCamera()
        {
            _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            GameObject root = transform.root.gameObject;

            _vcamCache = root.GetComponentInChildren<Unity.Cinemachine.CinemachineCamera>(true);
            if (_vcamCache == null) _vcamCache = FindFirstObjectByType<Unity.Cinemachine.CinemachineCamera>();

            if (_vcamCache != null)
            {
                _vcam3rdPerson = Tpf();

                if (CinemachineCameraTarget != null)
                {
                    _vcamCache.Follow = CinemachineCameraTarget.transform;
                    _vcamCache.LookAt = CinemachineCameraTarget.transform;
                    _vcamCache.enabled = CanExecuteLocalLogic;
                    _vcamCache.Priority = CanExecuteLocalLogic ? 100 : 0;
                }
            }
        }

        public void RefreshFunctionalComponents()
        {
            _jetpack = GetComponentInChildren<PropulsionController>(true);
            _shooter = GetComponentInChildren<ShootController>(true);
            _melee = GetComponentInChildren<MeleeController>(true);
            _shield = GetComponentInChildren<ShieldController>(true);

            _animator = null;
            _renderTransform = transform.Find("PlayerRender") ?? transform.Find("Render") ?? transform.Find("EnemyRender");

            if (_renderTransform != null)
            {
                _animator = _renderTransform.GetComponentInChildren<Animator>(true);
                _renderTransform.localPosition = new Vector3(0, -0.05f, 0);
                _renderTransform.localRotation = Quaternion.identity;
            }

            if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
            if (_animator == null) _animator = GetComponentInParent<Animator>();
            if (_animator == null) _animator = GetComponent<Animator>();

            _hasAnimator = _animator != null;
            if (_hasAnimator)
            {
                _animator.enabled = true;
                _animator.applyRootMotion = false;
                AssignAnimationIDs();
            }
            SetupCamera();
        }

        public void RefreshBodyReferences()
        {
            RefreshFunctionalComponents();
        }
        #endregion

        #region Input & Aiming Combat Logic
        private void RefreshInputActions()
        {
            if (_playerInput == null || _playerInput.actions == null) return;

            _moveAction = _playerInput.actions.FindAction("Move") ?? _playerInput.actions.FindAction("Player/Move");
            _lookAction = _playerInput.actions.FindAction("Look") ?? _playerInput.actions.FindAction("Player/Look");
            _jumpAction = _playerInput.actions.FindAction("Jump") ?? _playerInput.actions.FindAction("Player/Jump");
            _fireAction = _playerInput.actions.FindAction("Fire") ?? _playerInput.actions.FindAction("Player/Fire");
            _sprintAction = _playerInput.actions.FindAction("Sprint") ?? _playerInput.actions.FindAction("Player/Sprint");
            _aimAction = _playerInput.actions.FindAction("Aim") ?? _playerInput.actions.FindAction("Player/Aim");
            _hookAction = _playerInput.actions.FindAction("Hook") ?? _playerInput.actions.FindAction("Player/Hook");
            _crouchAction = _playerInput.actions.FindAction("Crouch") ?? _playerInput.actions.FindAction("Player/Crouch");
            _reloadAction = _playerInput.actions.FindAction("Reload") ?? _playerInput.actions.FindAction("Player/Reload");
            _nextWeaponAction = _playerInput.actions.FindAction("NextWeapon") ?? _playerInput.actions.FindAction("Player/NextWeapon");
        }

        private void UpdateInputState()
        {
            if (_moveAction != null) move = _moveAction.ReadValue<Vector2>();
            if (_lookAction != null) look = _lookAction.ReadValue<Vector2>();

            if (_jumpAction != null)
            {
                if (_jumpAction.WasPressedThisFrame()) jump = true;
                jumpHeld = _jumpAction.IsPressed();
                _isJumpHeld = jumpHeld;
            }

            if (_sprintAction != null && _sprintAction.IsPressed())
            {
                _shiftHoldTimer += Time.deltaTime;
                sprint = _shiftHoldTimer >= SprintHoldThreshold;
            }
            else
            {
                _shiftHoldTimer = 0f;
                sprint = false;
            }

            bool actionFire = _fireAction != null && _fireAction.WasPressedThisFrame();
            bool mouseFire = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            fire = actionFire || mouseFire;

            bool actionFireHeld = _fireAction != null && _fireAction.IsPressed();
            bool mouseFireHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
            fireHeld = actionFireHeld || mouseFireHeld;

            bool actionFireReleased = _fireAction != null && _fireAction.WasReleasedThisFrame();
            bool mouseFireReleased = Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
            fireReleased = actionFireReleased || mouseFireReleased;

            bool aimPressedThisFrame = (_aimAction != null && _aimAction.WasPressedThisFrame()) ||
                                       (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
            bool aimIsHeld = (_aimAction != null && _aimAction.IsPressed()) ||
                             (Mouse.current != null && Mouse.current.rightButton.isPressed);

            if (Keyboard.current != null && Cursor.lockState == CursorLockMode.Locked)
            {
                var k = Keyboard.current;
                if (k.digit1Key.wasPressedThisFrame)
                {
                    meleeMode = true;
                    if (_shield != null) _shield.SetShieldState(false);
                }
                else if (k.digit2Key.wasPressedThisFrame)
                {
                    meleeMode = false;
                    if (_shield != null) _shield.SetShieldState(false);
                }
                else if (k.digit3Key.wasPressedThisFrame) scopeMode = !scopeMode;
            }

            if (meleeMode)
            {
                aim = false;
                scope = false;

                if (_shield != null)
                {
                    if (toggleAim)
                    {
                        if (aimPressedThisFrame)
                        {
                            bool newState = !_shield.IsShieldActive;
                            _shield.SetShieldState(newState);
                        }
                    }
                    else
                    {
                        _shield.SetShieldState(aimIsHeld);
                    }
                }
            }
            else
            {
                if (toggleAim)
                {
                    if (aimPressedThisFrame) aim = !aim;
                }
                else
                {
                    aim = aimIsHeld;
                }
                scope = aim && scopeMode;
            }

            bool hookActionPressed = _hookAction != null && _hookAction.WasPressedThisFrame();
            bool keyGPressed = Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame;
            hook = hookActionPressed || keyGPressed;

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
                    var wk = Key.Digit1 + (i - 1);
                    if (i <= 3) continue;
                    if (Keyboard.current[wk].wasPressedThisFrame)
                    {
                        selectWeapon = i;
                        break;
                    }
                }
            }
        }

        private void ClearInput()
        {
            move = Vector2.zero;
            look = Vector2.zero;
            jump = jumpHeld = _isJumpHeld = sprint = false;
            fire = fireHeld = fireReleased = aim = scope = hook = crouch = reload = false;
            switchWeapon = selectWeapon = 0;
            if (_shield != null && meleeMode) _shield.SetShieldState(false);
        }

        private void HandleAimingAndCombat()
        {
            if (crosshairUI != null)
            {
                crosshairUI.SetActive(!(_gameCursor && Cursor.visible));
            }

            if (_vcamCache == null) SetupCamera();

            float az = aim && !scope ? aimZoomStrength : 0f;
            float zs = scope ? 10f : aimZoomSpeed;
            float normalDist = shoulderDistance * ZoomController.Factor;
            float targetFOV = scope ? aimFOV : Mathf.Lerp(normalFOV, shoulderAimFOV, az);

            if (_vcamCache != null)
            {
                _vcamCache.Lens.FieldOfView = Mathf.Lerp(_vcamCache.Lens.FieldOfView, targetFOV, Time.deltaTime * zs);

                if (_vcam3rdPerson == null)
                {
                    _vcam3rdPerson = Tpf();
                }

                if (_vcam3rdPerson != null)
                {
                    float targetDistance = scope ? aimCameraDistance : Mathf.Lerp(normalDist, shoulderAimDistance, az);
                    Vector3 targetShoulderOffset = scope ? aimShoulderOffset : Vector3.Lerp(shoulderOffset, shoulderAimOffset, az) + Vector3.up * cameraRise;
                    _vcam3rdPerson.CameraSide = 1f;

                    _vcam3rdPerson.CameraDistance = Mathf.Lerp(_vcam3rdPerson.CameraDistance, targetDistance, Time.deltaTime * zs);
                    _vcam3rdPerson.ShoulderOffset = Vector3.Lerp(_vcam3rdPerson.ShoulderOffset, targetShoulderOffset, Time.deltaTime * zs);
                }
            }

            if (_mainCamera != null && _mainCamera.TryGetComponent<Camera>(out var camComp))
            {
                camComp.fieldOfView = Mathf.Lerp(camComp.fieldOfView, targetFOV, Time.deltaTime * zs);
            }

            if (aim && hook)
            {
                TryFireHook();
                hook = false;
            }

            if (_melee != null) _melee.enabled = meleeMode;
            if (_shooter != null) _shooter.enabled = !meleeMode;

            if (meleeMode)
            {
                if (_melee != null && fire) _melee.PerformMeleeAction();
            }
            else
            {
                if (_shooter != null && (_shooter.isUnlocked || _shooter.Projectile != null) && (_shooter.HoldToFire ? fireHeld : fire))
                {
                    // Pasamos 'aim' (si está apuntando o no) al ShootController
                    _shooter.TryFire(aim);
                }
            }
        }

        private void TryFireHook()
        {
            if (hookProjectilePrefab == null) return;

            Transform spawnTransform = hookMuzzle != null ? hookMuzzle : (_shooter != null && _shooter.Muzzle != null ? _shooter.Muzzle.transform : transform);
            Vector3 spawnPos = spawnTransform.position;
            Vector3 aimDirection = _mainCamera != null ? _mainCamera.transform.forward : transform.forward;

            GameObject hookGO = Instantiate(hookProjectilePrefab, spawnPos, Quaternion.LookRotation(aimDirection));
            HookProjectileController hookComp = hookGO.GetComponent<HookProjectileController>();

            if (hookComp != null)
            {
                hookComp.Launch(gameObject, aimDirection, spawnTransform);
            }
        }

#if ENABLE_INPUT_SYSTEM
        public void OnJump(InputValue value)
        {
            _isJumpHeld = value.isPressed;
            if (CanExecuteLocalLogic && value.isPressed) jump = true;
        }

        public void OnLook(InputValue value)
        {
            if (cursorInputForLook && CanExecuteLocalLogic)
            {
                LookInput(value.Get<Vector2>());
            }
        }

        public void OnSprint(InputValue value)
        {
            if (CanExecuteLocalLogic) SprintInput(value.isPressed);
        }
#endif

        public void MoveInput(Vector2 newMoveDirection) => move = newMoveDirection;
        public void LookInput(Vector2 newLookDirection) => look = newLookDirection;
        public void JumpInput(bool newJumpState) { if (newJumpState) jump = true; }
        public void SprintInput(bool newSprintState) => sprint = newSprintState;

        private void OnApplicationFocus(bool hasFocus)
        {
            if (CanExecuteLocalLogic && !MenuGate.Busy) SetCursorState(cursorLocked);
        }

        private void SetCursorState(bool newState)
        {
            Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !newState;
        }
        #endregion

        #region Network Shooting Bridge
        public void RequestFire(GameObject prefab, Vector3 direction, Vector3 spawnPos, float damage, Team team, bool enableHoming)
        {
            // Mientras se apunta con clic derecho, los proyectiles viajan sin homing.
            // Sin apuntar, se conserva la configuración de homing del ShootController.
            enableHoming = enableHoming && !aim;
            if (IsNetworkActive)
            {
                FireServerRpc(direction, spawnPos, damage, team, enableHoming);
            }
            else
            {
                GameObject projObj = Instantiate(prefab, spawnPos, Quaternion.LookRotation(direction));
                ProjectileController projectile = projObj.GetComponent<ProjectileController>();
                if (projectile != null)
                {
                    projectile.enableHomingTracking = enableHoming;
                    projectile.Launch(gameObject, direction, damage, team);
                }
            }
        }

        [ServerRpc]
        private void FireServerRpc(Vector3 direction, Vector3 spawnPos, float damage, Team team, bool enableHoming)
        {
            GameObject projectilePrefab = null;

            var shooter = GetComponentInChildren<ShootController>();
            if (shooter != null)
            {
                projectilePrefab = shooter.Projectile;
            }

            if (projectilePrefab != null)
            {
                GameObject projObj = Instantiate(projectilePrefab, spawnPos, Quaternion.LookRotation(direction));
                ProjectileController projectile = projObj.GetComponent<ProjectileController>();
                if (projectile != null)
                {
                    projectile.enableHomingTracking = enableHoming;
                    projectile.Launch(gameObject, direction, damage, team);
                }

                if (projObj.TryGetComponent<NetworkObject>(out var netObj)) netObj.Spawn();
            }
        }
        #endregion

        #region Movement Logic
        private void GroundedCheck()
        {
            if (_verticalVelocity > 0.1f) { Grounded = false; return; }

            Vector3 spherePos = transform.position + Vector3.up * GroundedRadius;

            LayerMask mask = GroundLayers;
            if (mask == 0) mask = ~((1 << 3) | (1 << 2));

            Grounded = Physics.CheckSphere(spherePos, GroundedRadius * 1.1f, mask, QueryTriggerInteraction.Ignore)
                     || (_controller != null && _controller.isGrounded);
        }

        private void CameraRotation()
        {
            if (IsRespawning)
            {
                _cinemachineTargetYaw = 0f;
                _cinemachineTargetPitch = 0f;
                CinemachineCameraTarget.transform.position = _cameraStartingPosition;
                CinemachineCameraTarget.transform.rotation = _cameraStartingRotation;
                IsRespawning = false;
                return;
            }

            if (look.sqrMagnitude >= _threshold && !LockCameraPosition)
            {
                float deltaTimeMultiplier = (IsCurrentDeviceMouse) ? 0.5f : Time.deltaTime;

                _cinemachineTargetYaw += look.x * deltaTimeMultiplier * LookSensitivity.x * 2f;
                _cinemachineTargetPitch += look.y * deltaTimeMultiplier * LookSensitivity.y * 2f;
            }

            _cinemachineTargetYaw = ClampAngle(_cinemachineTargetYaw, float.MinValue, float.MaxValue);
            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

            if (CinemachineCameraTarget != null)
            {
                CinemachineCameraTarget.transform.rotation = Quaternion.Euler(
                    _cinemachineTargetPitch + CameraAngleOverride,
                    _cinemachineTargetYaw,
                    0.0f);
            }
        }

        private void Move()
        {
            float targetSpeed = aim ? MoveSpeed * aimMoveSpeedMultiplier : (sprint ? SprintSpeed : MoveSpeed);
            if (move == Vector2.zero) targetSpeed = 0.0f;

            _speed = Mathf.Lerp(_speed, targetSpeed, Time.deltaTime * SpeedChangeRate);
            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);

            if (_mainCamera == null) _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            float camYaw = (_mainCamera != null) ? _mainCamera.transform.eulerAngles.y : 0f;

            if (aim)
            {
                float rot = Mathf.SmoothDampAngle(transform.eulerAngles.y, camYaw, ref _rotationVelocity, Mathf.Min(RotationSmoothTime, 0.06f));
                transform.rotation = Quaternion.Euler(0.0f, rot, 0.0f);
                if (move != Vector2.zero) _moveDir = Quaternion.Euler(0.0f, camYaw, 0.0f) * new Vector3(move.x, 0f, move.y);
            }
            else if (move != Vector2.zero)
            {
                _targetRotation = Mathf.Atan2(move.x, move.y) * Mathf.Rad2Deg + camYaw;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
                _moveDir = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
            }

            _kb = Vector3.MoveTowards(_kb, Vector3.zero, 35f * Time.deltaTime);
            _controller.Move(_moveDir.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime + _kb * Time.deltaTime);
        }

        public void ApplyKnockback(Vector3 push)
        {
            if (IsNetworkActive && IsServer) KnockbackClientRpc(push); else Knock(push);
        }

        [ClientRpc]
        private void KnockbackClientRpc(Vector3 push) => Knock(push);

        private void Knock(Vector3 push)
        {
            CombatFx.Flash(gameObject);
            if (!CanExecuteLocalLogic) return;
            _kb = new Vector3(push.x, 0f, push.z);
            _verticalVelocity = Mathf.Max(_verticalVelocity, push.y);
        }

        private void JumpAndGravity()
        {
            if (_jetpack != null && _jetpack.ProcessFlight(_isJumpHeld, Grounded, ref _verticalVelocity))
            {
                jump = false;
                return;
            }

            if (Grounded)
            {
                _fallTimeoutDelta = FallTimeout;

                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -8f;
                    _jumpsRemaining = EnableDoubleJump ? 2 : 1;
                }

                if (jump && _jumpsRemaining > 0)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    _jumpsRemaining--;
                    if (_hasAnimIDJump) _animator.SetTrigger(_animIDJump);
                    jump = false;
                }
            }
            else
            {
                if (jump && EnableDoubleJump && _jumpsRemaining > 0)
                {
                    _verticalVelocity = Mathf.Sqrt(DoubleJumpHeight * -2f * Gravity);
                    _jumpsRemaining--;
                    if (_hasAnimIDJump) _animator.SetTrigger(_animIDJump);
                    jump = false;
                }
            }

            if (_verticalVelocity > -_terminalVelocity)
                _verticalVelocity += Gravity * (_verticalVelocity < 0f ? FallGravityMultiplier : 1f) * Time.deltaTime;

            jump = false;
        }
        #endregion

        #region Respawn Logic
        private void HandleRespawn()
        {
            if (transform.position.y < yThreshold) Respawn();
        }

        public void Respawn()
        {
            if (_controller != null) _controller.enabled = false;
            transform.position = _startingPosition;
            transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            if (_controller != null)
            {
                _controller.enabled = true;
                _verticalVelocity = 0f;
            }
            ResetCameraRotation(90f);
            if (respawnSound != null) AudioSource.PlayClipAtPoint(respawnSound, transform.position);
        }

        public void ResetCameraRotation(float targetYaw)
        {
            _cinemachineTargetYaw = targetYaw;
            _cinemachineTargetPitch = 0f;
            if (CinemachineCameraTarget != null)
                CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_cinemachineTargetPitch, _cinemachineTargetYaw, 0f);
        }
        #endregion

        #region Animation Helpers
        private void AssignAnimationIDs()
        {
            if (_hasAnimator)
            {
                _hasAnimIDSpeed = HasParameter(_animator, _animIDSpeed);
                _hasAnimIDIsGrounded = HasParameter(_animator, _animIDIsGrounded);
                _hasAnimIDJump = HasParameter(_animator, _animIDJump);
            }
        }

        private void UpdateAnimatorParameters()
        {
            if (!_hasAnimator || _animator == null || !_animator.gameObject.activeInHierarchy)
            {
                RefreshFunctionalComponents();
                if (!_hasAnimator) return;
            }

            if (!_animator.enabled) _animator.enabled = true;
            _animator.applyRootMotion = false;

            if (_renderTransform != null)
            {
                _renderTransform.localPosition = new Vector3(0, -0.05f, 0);
                _renderTransform.localRotation = Quaternion.identity;
            }

            float speedFactor = 0f;
            if (_animationBlend > 0.01f)
            {
                if (sprint)
                {
                    speedFactor = Mathf.Lerp(0.5f, 1.0f, (_animationBlend - MoveSpeed) / Mathf.Max(0.1f, SprintSpeed - MoveSpeed));
                }
                else
                {
                    speedFactor = Mathf.Lerp(0.1f, 0.5f, _animationBlend / MoveSpeed);
                }
            }

            if (_hasAnimIDSpeed) _animator.SetFloat(_animIDSpeed, speedFactor);
            if (_hasAnimIDIsGrounded) _animator.SetBool(_animIDIsGrounded, Grounded);
        }

        private bool HasParameter(Animator animator, int paramHash)
        {
            foreach (AnimatorControllerParameter param in animator.parameters)
                if (param.nameHash == paramHash) return true;
            return false;
        }
        #endregion

        #region Audio Helpers
        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f && FootstepAudioClips.Length > 0)
            {
                var index = Random.Range(0, FootstepAudioClips.Length);
                AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
            }
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f && LandingAudioClip != null)
                AudioSource.PlayClipAtPoint(LandingAudioClip, transform.TransformPoint(_controller.center), FootstepAudioVolume);
        }
        #endregion

        #region Math Helpers
        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }
        #endregion

        #region Debug
        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);
            Gizmos.color = Grounded ? transparentGreen : transparentRed;
            Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
        }
        #endregion
    }
}