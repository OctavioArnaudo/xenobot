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
    public class MovementController : NetworkBehaviour
    {
        #region Variables: Movement Settings
        [Header("Movement")]
        public float MoveSpeed = 2.0f;
        public float SprintSpeed = 5.335f;
        [Range(0.0f, 0.3f)] public float RotationSmoothTime = 0.12f;
        public float SpeedChangeRate = 10.0f;
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

        #region Variables: Input Data
        // Simplified input state for physics logic
        private Vector2 move;
        private bool jump;
        private bool jumpHeld;
        private bool sprint;
        #endregion

        #region Internal State
        private float _speed;
        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;
        private int _jumpsRemaining;

        // Animator Parameter Hashes
        private static readonly int _animIDSpeed = Animator.StringToHash("Speed");
        private static readonly int _animIDIsGrounded = Animator.StringToHash("isGrounded");

        private bool _hasAnimIDSpeed;
        private bool _hasAnimIDIsGrounded;

        private CharacterController _controller;
        private PlayerController _hub;
        private PropulsionController _jetpack;
        public float yThreshold = -5f; // Threshold for respawn detection


        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;
        #endregion

        #region Lifecycle
        private void Awake()
        {
            // Buscamos el Hub por todas las vías posibles
            _hub = GetComponentInParent<PlayerController>();
            if (_hub == null) _hub = PlayerController.LocalInstance;
        }

        private void Start()
        {
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;

            // Si el Hub no tiene el controller, lo buscamos en la raíz
            if (_hub != null && _hub.controller == null)
                _hub.controller = _hub.GetComponent<CharacterController>();

            _controller = _hub != null ? _hub.controller : GetComponentInParent<CharacterController>();

            AssignAnimationIDs();
        }

        public override void OnNetworkSpawn()
        {
            // ELIMINADO: Ya no desconectamos el módulo del padre.
            // Debe quedarse como hijo para que GetComponentInParent funcione siempre.
            _hub = GetComponentInParent<PlayerController>();
        }

        private void Update()
        {
            if (_hub == null) _hub = PlayerController.LocalInstance;
            if (!CanExecuteLocalLogic || _hub == null) return;

            // Forzamos la referencia al controller del Hub si el nuestro es null
            if (_controller == null) _controller = _hub.controller;
            if (_controller == null) _controller = _hub.GetComponent<CharacterController>();
            if (_controller == null) return;

            // Transferencia explícita de datos desde el Hub
            move = _hub.move;
            sprint = _hub.sprint;
            jump = _hub.jump;
            jumpHeld = _hub.jumpHeld;

            if (_controller == null) _controller = _hub.controller;
            if (_controller == null) return; // Si aún no hay controller, esperamos

            GroundedCheck();
            JumpAndGravity();
            Move();
            UpdateAnimatorParameters();

            // Consume single-frame inputs in hub after use
            if (jump) _hub.jump = false;
        }

        private void JumpAndGravity()
        {
            bool isUsingJetpack = false;
            if (_jetpack != null)
            {
                isUsingJetpack = _jetpack.ProcessFlight(jumpHeld, Grounded, ref _verticalVelocity);
                if (isUsingJetpack) jump = false;
            }

            if (isUsingJetpack) return;

            if (Grounded)
            {
                _fallTimeoutDelta = FallTimeout;

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
                }

                if (_jumpTimeoutDelta >= 0.0f) _jumpTimeoutDelta -= Time.deltaTime;
            }
            else
            {
                _jumpTimeoutDelta = JumpTimeout;
                if (_fallTimeoutDelta >= 0.0f) _fallTimeoutDelta -= Time.deltaTime;

                if (jump && EnableDoubleJump && _jumpsRemaining > 0)
                {
                    _verticalVelocity = Mathf.Sqrt(DoubleJumpHeight * -2f * Gravity);
                    _jumpsRemaining--;
                }

                if (_verticalVelocity > -_terminalVelocity)
                    _verticalVelocity += Gravity * Time.deltaTime;
            }
        }
        private void GroundedCheck()
        {
            if (_hub == null) return;
            // Aseguramos que GroundLayers no esté vacío por defecto (Capa 0 = Default)
            if (GroundLayers == 0) GroundLayers = 1;

            Vector3 spherePosition = new Vector3(_hub.transform.position.x, _hub.transform.position.y - GroundedOffset, _hub.transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
        }

        private void Move()
        {
            if (_hub == null || _controller == null) return;

            float targetSpeed = sprint ? SprintSpeed : MoveSpeed;
            if (move == Vector2.zero) targetSpeed = 0.0f;

            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
            float speedOffset = 0.1f;
            float inputMagnitude = _hub.analogMovement ? move.magnitude : 1f;

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
                GameObject cam = _hub.mainCamera != null ? _hub.mainCamera : Camera.main.gameObject;
                if (cam != null)
                {
                    _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + cam.transform.eulerAngles.y;
                    float rotation = Mathf.SmoothDampAngle(_hub.transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
                    _hub.transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
                }
            }

            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
            _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
        }

        public void RefreshFunctionalComponents()
        {
            _jetpack = GetComponentInChildren<PropulsionController>();
            AssignAnimationIDs();
        }
        #endregion

        #region Animation Helpers
        private void AssignAnimationIDs()
        {
            if (_hub != null && _hub.animator != null)
            {
                _hasAnimIDSpeed = HasParameter(_hub.animator, _animIDSpeed);
                _hasAnimIDIsGrounded = HasParameter(_hub.animator, _animIDIsGrounded);
            }
        }

        private void UpdateAnimatorParameters()
        {
            if (_hub == null || _hub.animator == null) return;
            if (_hasAnimIDSpeed) _hub.animator.SetFloat(_animIDSpeed, _animationBlend);
            if (_hasAnimIDIsGrounded) _hub.animator.SetBool(_animIDIsGrounded, Grounded);
        }

        private bool HasParameter(Animator animator, int paramHash)
        {
            foreach (AnimatorControllerParameter param in animator.parameters)
                if (param.nameHash == paramHash) return true;
            return false;
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