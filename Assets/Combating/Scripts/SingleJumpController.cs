using UnityEngine;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Unity.Netcode;
using Unity.Cinemachine;
using Crafting.Scripts;

namespace Combating.Scripts
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class SingleJumpController : NetworkBehaviour
    {
        #region Variables: Movement Settings
        [Header("Movement")]
        public float MoveSpeed = 2.0f;
        public float SprintSpeed = 5.335f;
        [Range(0.0f, 0.3f)] public float RotationSmoothTime = 0.12f;
        public float SpeedChangeRate = 10.0f;
        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;
        public float JumpHeight = 1.2f;

        [Header("Double Jump")]
        public bool EnableDoubleJump = true;
        public float DoubleJumpHeight = 1.2f;

        public float Gravity = -15.0f;
        public float JumpTimeout = 0.50f;
        public float FallTimeout = 0.15f;
        #endregion

        #region Variables: Ground Check
        [Header("Ground Check")]
        public bool Grounded = true;
        public float GroundedOffset = -0.14f;
        public float GroundedRadius = 0.28f;
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
        public bool crouch;
        public bool reload;
        public int switchWeapon;
        public int selectWeapon;
        public bool analogMovement;
        public bool cursorLocked = true;
        public bool cursorInputForLook = true;
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
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;
        private int _jumpsRemaining;

        // Animator Parameter Hashes (match Animator Controller exactly)
        private static readonly int _animIDSpeed = Animator.StringToHash("Speed");
        private static readonly int _animIDIsGrounded = Animator.StringToHash("isGrounded");

        private bool _hasAnimIDSpeed;
        private bool _hasAnimIDIsGrounded;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
        private Animator _animator;
        private CharacterController _controller;
        private GameObject _mainCamera;

        private FuelController _health;
        private PropulsionController _jetpack;
        private InventoryController _inventory;

        private bool _isJumpHeld;
        private const float _threshold = 0.01f;
        private bool _hasAnimator;

        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _fireAction;
        private InputAction _sprintAction;
        private InputAction _aimAction;
        private InputAction _crouchAction;
        private InputAction _reloadAction;
        private InputAction _nextWeaponAction;

        private bool IsCurrentDeviceMouse => _playerInput != null && _playerInput.currentControlScheme == "KeyboardMouse";
        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;
        #endregion

        #region Lifecycle
        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<FuelController>();
            _inventory = GetComponent<InventoryController>();
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
            if (IsOwner)
            {
                NetworkObject netObj = GetComponentInParent<NetworkObject>();
                if (netObj == null) netObj = GetComponent<NetworkObject>();
                if (netObj != null) netObj.transform.SetParent(null);

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

        private void Update()
        {
            if (!CanExecuteLocalLogic) return;

#if ENABLE_INPUT_SYSTEM
            if (_fireAction == null) RefreshInputActions();
            UpdateInputState();
#endif

            GroundedCheck();
            HandleRespawn();
            JumpAndGravity();
            Move();
            UpdateAnimatorParameters();
        }

        private void RefreshInputActions()
        {
            if (_playerInput == null || _playerInput.actions == null) return;

            _moveAction = _playerInput.actions.FindAction("Move") ?? _playerInput.actions.FindAction("Player/Move");
            _lookAction = _playerInput.actions.FindAction("Look") ?? _playerInput.actions.FindAction("Player/Look");
            _jumpAction = _playerInput.actions.FindAction("Jump") ?? _playerInput.actions.FindAction("Player/Jump");
            _fireAction = _playerInput.actions.FindAction("Fire") ?? _playerInput.actions.FindAction("Player/Fire");
            _sprintAction = _playerInput.actions.FindAction("Sprint") ?? _playerInput.actions.FindAction("Player/Sprint");
            _aimAction = _playerInput.actions.FindAction("Aim") ?? _playerInput.actions.FindAction("Player/Aim");
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
                Transform bone = anim.GetBoneTransform(HumanBodyBones.Head) ??
                                 anim.GetBoneTransform(HumanBodyBones.Neck) ??
                                 anim.GetBoneTransform(HumanBodyBones.Chest) ??
                                 anim.transform.Find("head") ??
                                 anim.transform.Find("Head") ??
                                 anim.transform.Find("spine") ??
                                 anim.transform.Find("Chest");

                if (bone != null)
                {
                    CinemachineCameraTarget.transform.position = bone.position + Vector3.up * 0.4f;
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

        private void SetupCamera()
        {
            _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            GameObject root = transform.root.gameObject;
            CinemachineCamera vcam = null;

            foreach (var cam in root.GetComponentsInChildren<CinemachineCamera>(true))
            {
                if (cam.transform.IsChildOf(root.transform))
                {
                    vcam = cam;
                    break;
                }
            }

            if (vcam != null && CinemachineCameraTarget != null)
            {
                vcam.Follow = CinemachineCameraTarget.transform;
                vcam.LookAt = CinemachineCameraTarget.transform;
                vcam.enabled = CanExecuteLocalLogic;
                vcam.Priority = CanExecuteLocalLogic ? 100 : 0;
            }
        }

        public void RefreshFunctionalComponents()
        {
            _jetpack = GetComponentInChildren<PropulsionController>();
            _animator = GetComponentInChildren<Animator>();
            _hasAnimator = _animator != null;
            AssignAnimationIDs();
            SetupCamera();
        }

        public void RefreshBodyReferences()
        {
            RefreshFunctionalComponents();
        }
        #endregion

        #region Input Handling
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

        public void InputMove(InputValue value) => MoveInput(value.Get<Vector2>());
        public void InputLook(InputValue value) => LookInput(value.Get<Vector2>());
        public void InputJump(InputValue value)
        {
            if (value.isPressed) jump = true;
            jumpHeld = value.isPressed;
        }
        public void InputSprint(InputValue value) => SprintInput(value.isPressed);
        public void InputFire(InputValue value)
        {
            bool isPressed = value.isPressed;
            if (isPressed) fire = true;
            fireHeld = isPressed;
            fireReleased = !isPressed;
        }
        public void InputAim(InputValue value) => aim = value.isPressed;
        public void InputCrouch(InputValue value) => crouch = value.isPressed;
        public void InputReload(InputValue value) => reload = value.isPressed;
        public void InputNextWeapon(InputValue value) => switchWeapon = (int)value.Get<float>();
        public void InputSelectWeapon(InputValue value) => selectWeapon = (int)value.Get<float>();

        private void OnApplicationFocus(bool hasFocus)
        {
            if (CanExecuteLocalLogic) SetCursorState(cursorLocked);
        }

        private void SetCursorState(bool newState)
        {
            Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !newState;
        }
        #endregion

        #region Network Shooting Bridge
        public void RequestFire(ProjectileController prefab, Vector3 direction, Vector3 spawnPos, float damage, Team team)
        {
            if (IsNetworkActive)
            {
                FireServerRpc(direction, spawnPos, damage, team);
            }
            else
            {
                ProjectileController projectile = Instantiate(prefab, spawnPos, Quaternion.LookRotation(direction));
                projectile.Launch(gameObject, direction, damage, team);
            }
        }

        [ServerRpc]
        private void FireServerRpc(Vector3 direction, Vector3 spawnPos, float damage, Team team)
        {
            ProjectileController projectilePrefab = null;

            var shooter = GetComponentInChildren<ShootController>();
            if (shooter != null)
            {
                projectilePrefab = shooter.ProjectilePrefab;
            }

            if (projectilePrefab != null)
            {
                ProjectileController instance = Instantiate(projectilePrefab, spawnPos, Quaternion.LookRotation(direction));
                instance.Launch(gameObject, direction, damage, team);
                instance.GetComponent<NetworkObject>().Spawn();
            }
            else
            {
                Debug.LogWarning("[PlayerController] El servidor no encontró un ShootController con proyectil asignado.");
            }
        }
        #endregion

        #region Movement Logic
        private void GroundedCheck()
        {
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
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
            float targetSpeed = sprint ? SprintSpeed : MoveSpeed;
            if (move == Vector2.zero) targetSpeed = 0.0f;

            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
            float speedOffset = 0.1f;
            float inputMagnitude = analogMovement ? move.magnitude : 1f;

            if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            Vector3 inputDirection = new Vector3(move.x, 0.0f, move.y).normalized;

            if (move != Vector2.zero)
            {
                if (_mainCamera == null) _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");

                if (_mainCamera != null)
                {
                    _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + _mainCamera.transform.eulerAngles.y;
                    float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
                    transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
                }
            }

            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
            _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
        }

        private void JumpAndGravity()
        {
            bool isUsingJetpack = false;
            if (_jetpack != null)
            {
                isUsingJetpack = _jetpack.ProcessFlight(_isJumpHeld, Grounded, ref _verticalVelocity);
                if (isUsingJetpack) jump = false;
            }

            if (isUsingJetpack) return;

            if (Grounded)
            {
                _fallTimeoutDelta = FallTimeout;

                // Solo reinicia saltos cuando realmente está cayendo o reposando en el suelo
                if (_verticalVelocity <= 0.0f)
                {
                    _verticalVelocity = -2f;
                    _jumpsRemaining = EnableDoubleJump ? 2 : 1;
                }

                if (jump && _jumpTimeoutDelta <= 0.0f && _jumpsRemaining > 0)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    _jumpsRemaining--;
                    _jumpTimeoutDelta = JumpTimeout;
                    jump = false;
                }

                if (_jumpTimeoutDelta >= 0.0f) _jumpTimeoutDelta -= Time.deltaTime;
            }
            else
            {
                _jumpTimeoutDelta = JumpTimeout;
                if (_fallTimeoutDelta >= 0.0f) _fallTimeoutDelta -= Time.deltaTime;

                // Doble salto en el aire
                if (jump && EnableDoubleJump && _jumpsRemaining > 0)
                {
                    _verticalVelocity = Mathf.Sqrt(DoubleJumpHeight * -2f * Gravity);
                    _jumpsRemaining--;
                    jump = false;
                }

                if (_verticalVelocity > -_terminalVelocity)
                    _verticalVelocity += Gravity * Time.deltaTime;

                jump = false;
            }
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
            }
        }

        private void UpdateAnimatorParameters()
        {
            if (!_hasAnimator) return;

            if (_hasAnimIDSpeed) _animator.SetFloat(_animIDSpeed, _animationBlend);
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