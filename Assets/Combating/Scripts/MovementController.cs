using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class MovementController : MonoBehaviour, IModular
    {
        // Physical Reliability Constants
        private const float MoveSpeed = 10.0f;
        private const float SpeedChangeRate = 12.0f;
        private const float RotationSmoothTime = 0.08f;

        private float _speed;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;

        private CharacterController _controller;
        private ModularController _hub;
        private Transform _renderTransform;

        private bool HasInputAuthority => _hub != null && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || _hub.IsOwner);

        private void Awake()
        {
            // Hub will call Bind() during assembly.
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);
                _controller = _hub.controller ?? _hub.GetComponent<CharacterController>();
                OnRefreshModule();
            }
        }

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                _renderTransform = (_hub.activeModel != null) ? _hub.activeModel.transform : _hub.renderRoot;
                _controller = _hub.controller ?? _hub.GetComponent<CharacterController>();
            }
        }

        private void Start()
        {
            if (_hub == null) _hub = Testing.Scripts.PlayerController.LocalInstance;
            if (_hub != null && _renderTransform == null) _renderTransform = _hub.renderRoot;
        }

        private void Update()
        {
            if (_hub == null) return;
            if (!HasInputAuthority) return;

            if (_controller == null) _controller = _hub.controller ?? _hub.GetComponent<CharacterController>();
            if (_controller == null) return;

            // Physical state is now read directly from Hub
            float verticalVelocity = _hub.VerticalVelocity;
            bool isGrounded = _hub.IsGrounded;

            // Handle combined movement
            if (_hub is Testing.Scripts.PlayerController playerHub)
            {
                var propulsion = _hub.GetModule<PropulsionController>();
                if (propulsion != null)
                {
                    propulsion.ProcessFlight(playerHub.jumpHeld, isGrounded, ref verticalVelocity);
                    _hub.VerticalVelocity = verticalVelocity; // Update Hub state
                }

                ApplyMovement(playerHub, verticalVelocity, isGrounded);
            }
            else
            {
                Vector3 motion = Vector3.up * verticalVelocity;
                _controller.Move(motion * Time.deltaTime);
            }
        }

        private void ApplyMovement(Testing.Scripts.PlayerController player, float verticalVelocity, bool isGrounded)
        {
            float targetSpeed = player.sprint ? MoveSpeed * 2.5f : MoveSpeed;
            if (player.move == Vector2.zero) targetSpeed = 0.0f;

            // Use MoveTowards for smoother, linear acceleration/deceleration
            _speed = Mathf.MoveTowards(_speed, targetSpeed, Time.deltaTime * MoveSpeed * SpeedChangeRate);

            if (player.move != Vector2.zero)
            {
                var camCtrl = _hub.GetModule<CameraController>();
                float camYaw = (camCtrl != null) ? camCtrl.Yaw : 0;

                float inputRotation = Mathf.Atan2(player.move.x, player.move.y) * Mathf.Rad2Deg + camYaw;

                // Smooth rotation for BOTH rendering and physical direction
                _targetRotation = Mathf.SmoothDampAngle(_targetRotation, inputRotation, ref _rotationVelocity, 0.08f);

                if (_renderTransform != null)
                {
                    _renderTransform.rotation = Quaternion.Euler(0, _targetRotation, 0);
                }
            }

            // The movement direction now follows the smoothed target rotation
            Vector3 moveDir = Quaternion.Euler(0, _targetRotation, 0) * Vector3.forward;
            if (player.move == Vector2.zero && _speed < 0.1f) moveDir = Vector3.zero;

            _hub.HorizontalSpeed = _speed; // Report speed to Hub for animations

            Vector3 finalMotion = (moveDir * _speed) + (Vector3.up * verticalVelocity);

            // Execute move
            _controller.Move(finalMotion * Time.deltaTime);

            // If we hit a ceiling, kill upward velocity
            if ((_controller.collisionFlags & CollisionFlags.Above) != 0 && _hub.VerticalVelocity > 0)
            {
                _hub.VerticalVelocity = 0;
            }
        }

        private bool HasParameter(Animator anim, string paramName)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return false;
            foreach (AnimatorControllerParameter param in anim.parameters)
            {
                if (param.name == paramName) return true;
            }
            return false;
        }

        public void ResetPhysics()
        {
            if (_hub != null) _hub.VerticalVelocity = 0;
            _speed = 0;
        }

        public void RefreshFunctionalComponents() { _hub = GetComponentInParent<ModularController>(); _controller = null; }
    }
}
