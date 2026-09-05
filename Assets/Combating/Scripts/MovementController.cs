using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class MovementController : NetworkBehaviour, IModular
    {
        [Header("Settings")]
        public float MoveSpeed = 10.0f;
        public float SpeedChangeRate = 12.0f;

        private float _speed;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;

        private CharacterController _controller;
        private ModularController _hub;
        private Transform _renderTransform;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool HasInputAuthority => _hub != null && (!IsNetworkActive || _hub.IsOwner);

        private void Awake()
        {
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
            if (_hub != null) Bind(_hub);
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
            if (_hub == null) _hub = PlayerController.LocalInstance;
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
            if (_hub is PlayerController playerHub)
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

        private void ApplyMovement(PlayerController player, float verticalVelocity, bool isGrounded)
        {
            float targetSpeed = player.sprint ? MoveSpeed * 2.5f : MoveSpeed;
            if (player.move == Vector2.zero) targetSpeed = 0.0f;

            _speed = Mathf.Lerp(_speed, targetSpeed, Time.deltaTime * SpeedChangeRate);

            if (player.move != Vector2.zero)
            {
                var camCtrl = _hub.GetModule<CameraController>();
                float camYaw = (camCtrl != null) ? camCtrl.Yaw : 0;

                float inputRotation = Mathf.Atan2(player.move.x, player.move.y) * Mathf.Rad2Deg + camYaw;

                if (_renderTransform != null)
                {
                    float rotation = Mathf.SmoothDampAngle(_renderTransform.eulerAngles.y, inputRotation, ref _rotationVelocity, 0.03f);
                    _renderTransform.rotation = Quaternion.Euler(0, rotation, 0);
                }
                _targetRotation = inputRotation;
            }

            Vector3 moveDir = Quaternion.Euler(0, _targetRotation, 0) * Vector3.forward;
            if (player.move == Vector2.zero) moveDir = Vector3.zero;

            Vector3 finalMotion = (moveDir * _speed) + (Vector3.up * verticalVelocity);
            _controller.Move(finalMotion * Time.deltaTime);

            if (player.animator != null)
            {
                player.animator.SetFloat("Speed", _speed);
                player.animator.SetBool("isGrounded", isGrounded);

                if (player.jump && HasParameter(player.animator, "Jump")) player.animator.SetBool("Jump", true);
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
