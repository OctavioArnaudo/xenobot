using UnityEngine;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Unity.Netcode;
using Unity.Cinemachine;

namespace Xenobot.Movement
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class PlayerController : NetworkBehaviour
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
        public bool sprint;
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

        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;

        private bool _hasAnimIDSpeed;
        private bool _hasAnimIDGrounded;
        private bool _hasAnimIDJump;
        private bool _hasAnimIDFreeFall;
        private bool _hasAnimIDMotionSpeed;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
        private Animator _animator;
        private CharacterController _controller;
        private GameObject _mainCamera;
        private const float _threshold = 0.01f;
        private bool _hasAnimator;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return _playerInput != null && _playerInput.currentControlScheme == "KeyboardMouse";
#else
                return false;
#endif
            }
        }

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;
        #endregion

        #region Lifecycle
        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (_mainCamera == null)
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");

            if (CanExecuteLocalLogic)
            {
                SetCursorState(cursorLocked);
            }
        }

        private void Start()
        {
            _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;

            _animator = GetComponentInChildren<Animator>();
            _hasAnimator = _animator != null;

#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
#endif

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
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                // Buscamos el objeto raíz que tiene el NetworkObject
                NetworkObject netObj = GetComponentInParent<NetworkObject>();
                if (netObj == null) netObj = GetComponent<NetworkObject>();

                if (netObj != null)
                {
                    netObj.transform.SetParent(null);
                }

                SetupPlayerLocal();

                // Teletransportamos al punto de spawn de la escena actual de inmediato
                TeleportToSceneSpawn(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

                UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            }
            else
            {
                DisableLocalComponents();
                // IMPORTANTE: Desactivamos el CharacterController y la cámara en copias remotas
                if (_controller != null) _controller.enabled = false;

                // Desactivar cualquier cámara de Cinemachine que pudiera estar en este prefab
                var vcam = GetComponentInChildren<Unity.Cinemachine.CinemachineCamera>();
                if (vcam != null) vcam.enabled = false;
            }

            Debug.Log($"[PlayerController] Spawned: {gameObject.name} | NetID: {NetworkObjectId} | Owner: {IsOwner}");
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
            // Buscamos si hay una configuración específica para esta escena
            var config = SceneSpawns.Find(s => s.SceneName == sceneName);
            if (string.IsNullOrEmpty(config.SpawnPointTag)) return;

            // Buscamos todos los objetos con ese Tag en la escena
            GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag(config.SpawnPointTag);
            if (spawnPoints.Length > 0)
            {
                // Usamos el OwnerClientId para elegir un punto distinto para cada jugador
                // (Si solo hay un punto, todos irán al mismo, si hay varios se repartirán)
                int index = (int)(OwnerClientId % (ulong)spawnPoints.Length);
                Transform targetSpawn = spawnPoints[index].transform;

                // Desactivamos el CharacterController momentáneamente para permitir el teletransporte
                if (_controller != null) _controller.enabled = false;

                transform.position = targetSpawn.position;
                transform.rotation = targetSpawn.rotation;

                // Actualizamos las posiciones de inicio para el sistema de Respawn (caídas al vacío)
                _startingPosition = transform.position;
                _startingRotation = transform.rotation;

                if (_controller != null) _controller.enabled = true;

                Debug.Log($"[PlayerController] Teletransportado a punto de spawn: {targetSpawn.name} en escena: {sceneName}");
            }
            else
            {
                Debug.LogWarning($"[PlayerController] No se encontraron objetos con el Tag '{config.SpawnPointTag}' en la escena {sceneName}");
            }
        }

        private void Update()
        {
            if (!CanExecuteLocalLogic) return;

            HandleRespawn();
            JumpAndGravity();
            GroundedCheck();
            Move();
        }

        private void LateUpdate()
        {
            if (!CanExecuteLocalLogic) return;

            CameraRotation();
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

            // Buscamos la cámara recorriendo la jerarquía desde la raíz del objeto instanciado
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

                // Solo activamos la cámara si somos el dueño
                vcam.enabled = IsOwner;

                // Prioridad absoluta para la cámara del dueño
                vcam.Priority = IsOwner ? 100 : 0;

                Debug.Log($"[PlayerController] CinemachineCamera ({vcam.gameObject.name}) configurada para {gameObject.name} (Owner: {IsOwner})");
            }
            else
            {
                Debug.LogError($"[PlayerController] ERROR FATAL: No se encontró CinemachineCamera en {root.name}");
            }
        }
        #endregion

        #region Input Handling
#if ENABLE_INPUT_SYSTEM
        public void OnMove(InputValue value)
        {
            if (CanExecuteLocalLogic) MoveInput(value.Get<Vector2>());
        }

        public void OnLook(InputValue value)
        {
            if (cursorInputForLook && CanExecuteLocalLogic)
            {
                LookInput(value.Get<Vector2>());
            }
        }

        public void OnJump(InputValue value)
        {
            if (CanExecuteLocalLogic) JumpInput(value.isPressed);
        }

        public void OnSprint(InputValue value)
        {
            if (CanExecuteLocalLogic) SprintInput(value.isPressed);
        }
#endif

        public void MoveInput(Vector2 newMoveDirection) => move = newMoveDirection;
        public void LookInput(Vector2 newLookDirection) => look = newLookDirection;
        public void JumpInput(bool newJumpState) => jump = newJumpState;
        public void SprintInput(bool newSprintState) => sprint = newSprintState;

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

        #region Movement Logic
        private void GroundedCheck()
        {
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);

            if (_hasAnimator && _hasAnimIDGrounded)
                _animator.SetBool(_animIDGrounded, Grounded);
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
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;
                _cinemachineTargetYaw += look.x * deltaTimeMultiplier * LookSensitivity.x;
                _cinemachineTargetPitch += look.y * deltaTimeMultiplier * LookSensitivity.y;
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
                }

                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }

            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
            _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

            if (_hasAnimator)
            {
                if (_hasAnimIDSpeed) _animator.SetFloat(_animIDSpeed, _animationBlend);
                if (_hasAnimIDMotionSpeed) _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                _fallTimeoutDelta = FallTimeout;
                if (_hasAnimator)
                {
                    if (_hasAnimIDJump) _animator.SetBool(_animIDJump, false);
                    if (_hasAnimIDFreeFall) _animator.SetBool(_animIDFreeFall, false);
                }

                if (_verticalVelocity < 0.0f) _verticalVelocity = -2f;

                if (jump && _jumpTimeoutDelta <= 0.0f)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    if (_hasAnimator && _hasAnimIDJump) _animator.SetBool(_animIDJump, true);
                }

                if (_jumpTimeoutDelta >= 0.0f) _jumpTimeoutDelta -= Time.deltaTime;
            }
            else
            {
                _jumpTimeoutDelta = JumpTimeout;
                if (_fallTimeoutDelta >= 0.0f) _fallTimeoutDelta -= Time.deltaTime;
                else if (_hasAnimator && _hasAnimIDFreeFall) _animator.SetBool(_animIDFreeFall, true);
                jump = false;
            }

            if (_verticalVelocity < _terminalVelocity) _verticalVelocity += Gravity * Time.deltaTime;
        }
        #endregion

        #region Respawn Logic
        private void HandleRespawn()
        {
            if (transform.position.y < yThreshold)
                Respawn();
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

            if (respawnSound != null)
                AudioSource.PlayClipAtPoint(respawnSound, transform.position);
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
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");

            if (_hasAnimator)
            {
                _hasAnimIDSpeed = HasParameter(_animator, _animIDSpeed);
                _hasAnimIDGrounded = HasParameter(_animator, _animIDGrounded);
                _hasAnimIDJump = HasParameter(_animator, _animIDJump);
                _hasAnimIDFreeFall = HasParameter(_animator, _animIDFreeFall);
                _hasAnimIDMotionSpeed = HasParameter(_animator, _animIDMotionSpeed);
            }
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
