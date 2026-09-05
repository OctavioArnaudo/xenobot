using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class MovementController : NetworkBehaviour, IPlayer
    {
        [Header("Settings")]
        public float MoveSpeed = 10.0f;
        public float SpeedChangeRate = 12.0f;
        public float Gravity = -35.0f;
        public LayerMask GroundLayers = 1;

        private float _speed;
        private float _verticalVelocity;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;

        private CharacterController _controller;
        private PlayerController _hub;
        private Transform _renderTransform;
        private bool _isGrounded;

        private bool HasInputAuthority => _hub != null && (!NetworkManager.Singleton.IsListening || _hub.IsOwner);

        private void Awake()
        {
            if (_hub == null) _hub = GetComponentInParent<PlayerController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(PlayerController hub)
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
                // Priorizar el transform del modelo activo, no el contenedor raíz
                _renderTransform = (_hub.activeModel != null) ? _hub.activeModel.transform : _hub.renderRoot;
                _controller = _hub.controller ?? _hub.GetComponent<CharacterController>();
            }
        }

        private void Start()
        {
            if (_hub == null) _hub = PlayerController.LocalInstance;
            if (_hub != null) _renderTransform = _hub.transform.Find("PlayerRender");
        }

        private void Update()
        {
            if (_hub == null) _hub = PlayerController.LocalInstance;
            if (_hub == null || !HasInputAuthority) return;

            if (_controller == null) _controller = _hub.controller ?? _hub.GetComponent<CharacterController>();
            if (_controller == null) return;

            // _renderTransform is now maintained by OnRefreshModule() via Hub.renderRoot

            ApplyPhysics();
            ApplyMovement();
        }

        private void ApplyPhysics()
        {
            _isGrounded = _controller.isGrounded || Physics.CheckSphere(_hub.transform.position, 0.3f, GroundLayers, QueryTriggerInteraction.Ignore);

            if (_isGrounded)
            {
                if (_verticalVelocity < 0) _verticalVelocity = -2f;
                if (_hub.jump)
                {
                    _verticalVelocity = Mathf.Sqrt(4.0f * -2f * Gravity);
                    _hub.jump = false;
                }
            }
            else
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        private void ApplyMovement()
        {
            // Ajuste de velocidades: 25 base, 62.5 en sprint
            float targetSpeed = _hub.sprint ? MoveSpeed * 2.5f : MoveSpeed;
            if (_hub.move == Vector2.zero) targetSpeed = 0.0f;

            _speed = Mathf.Lerp(_speed, targetSpeed, Time.deltaTime * SpeedChangeRate);

            if (_hub.move != Vector2.zero)
            {
                // ESTABILIDAD TOTAL: Usamos la propiedad Yaw limpia del módulo
                var camCtrl = _hub.GetModule<CameraController>();
                float camYaw = (camCtrl != null) ? camCtrl.Yaw : 0;

                float inputRotation = Mathf.Atan2(_hub.move.x, _hub.move.y) * Mathf.Rad2Deg + camYaw;

                if (_renderTransform != null)
                {
                    // Giro ultra-rápido (0.03s)
                    float rotation = Mathf.SmoothDampAngle(_renderTransform.eulerAngles.y, inputRotation, ref _rotationVelocity, 0.03f);
                    _renderTransform.rotation = Quaternion.Euler(0, rotation, 0);
                }
                _targetRotation = inputRotation;
            }

            Vector3 moveDir = Quaternion.Euler(0, _targetRotation, 0) * Vector3.forward;
            if (_hub.move == Vector2.zero) moveDir = Vector3.zero;

            Vector3 finalMotion = (moveDir * _speed) + (Vector3.up * _verticalVelocity);
            _controller.Move(finalMotion * Time.deltaTime);

            // LOGICA DE ANIMACIÓN: Sincronizada con "New Animator Controller 1"
            if (_hub.animator != null)
            {
                _hub.animator.SetFloat("Speed", _speed);
                _hub.animator.SetBool("isGrounded", _isGrounded);

                // Solo intentamos setear lo que existe para evitar errores en consola
                if (_hub.jump && HasParameter(_hub.animator, "Jump")) _hub.animator.SetBool("Jump", true);
            }
        }

        // Método auxiliar para evitar errores de "Parameter not found"
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
            _verticalVelocity = 0;
            _speed = 0;
        }

        public void RefreshFunctionalComponents() { _hub = GetComponentInParent<PlayerController>(); _controller = null; }
    }
}
