using UnityEngine;
using Unity.Netcode;
using NGO.Gameplay.Base;
using StarterAssets;

namespace NGO.Gameplay.Networking
{
    public class WalkPlayerController : PlayerActionController
    {
        [Header("Walking Settings")]
        public float MoveSpeed = 10.0f;
        public float SpeedChangeRate = 50.0f;
        public float RotationSmoothTime = 0.05f;

        [HideInInspector] public float SpeedMultiplier = 1.0f;

        private float _speed;
        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;

        private CharacterController _controller;
        private Animator _animator;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;
        private bool _hasAnimator;

        private int _animIDSpeed;
        private int _animIDMotionSpeed;

        public override void Initialize(Unity.Netcode.NetworkObject root)
        {
            base.Initialize(root);
            _controller = root.GetComponent<CharacterController>();
            _animator = root.GetComponentInChildren<Animator>();
            _input = root.GetComponentInChildren<StarterAssetsInputs>();
            _hasAnimator = _animator != null;

            if (_mainCamera == null) _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");

            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        }

        public override void OnActionTriggered() { }

        public override void OnTick()
        {
            if (!IsOwner || _input == null) return;
            Move();
        }

        private void Move()
        {
            float targetSpeed = MoveSpeed * SpeedMultiplier;
            if (_input.move == Vector2.zero) targetSpeed = 0.0f;

            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);
            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);

            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

            if (_input.move != Vector2.zero)
            {
                if (_mainCamera == null) _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
                if (_mainCamera == null) return;

                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + _mainCamera.transform.eulerAngles.y;
                float rotation = Mathf.SmoothDampAngle(playerRoot.transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
                playerRoot.transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }

            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
            _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime));

            if (_hasAnimator)
            {
                _animator.SetFloat(_animIDSpeed, _animationBlend);
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }
    }
}
