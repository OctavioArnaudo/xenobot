using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Cinemachine;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class CameraController : MonoBehaviour, IModular
    {
        [Header("Settings")]
        public Vector2 LookSensitivity = new Vector2(1.0f, 0.8f);
        public float TopClamp = 85.0f;
        public float BottomClamp = -60.0f;
        public float MinLookAtHeight = 0.5f;

        [Header("Over-The-Shoulder Settings")]
        public Vector3 DefaultShoulderOffset = new Vector3(0.75f, 0.2f, -2.5f);
        public Vector3 AimShoulderOffset = new Vector3(0.5f, 0.1f, -1.5f);
        public float NormalFOV = 60f;
        public float AimFOV = 45f;
        public float TransitionSpeed = 10f;

        [Header("Player Rotation Alignment")]
        public bool AlignPlayerWithCamera = true;
        public float PlayerRotationSpeed = 15f;

        public float Yaw => _yaw;
        public float Pitch => _pitch;

        private float _yaw;
        private float _pitch;
        private float _shoulderSide = 1f;
        private ModularController _hub;
        private GameObject _target;
        private CinemachineCamera _vcam;
        private CinemachineBrain _brain;
        private Vector3 _currentOffset;

        // Verificación con namespace correcto
        private bool HasInputAuthority =>            (_hub.GetComponent<PlayerController>() != null) &&
                (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || _hub.IsOwner);

        void Awake()
        {
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
            _currentOffset = DefaultShoulderOffset;
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);
                OnRefreshModule();
            }
        }

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                _target = _hub.cameraTarget;
                RefreshCameraLink();
            }
        }

        private void LateUpdate()
        {
            if (_hub == null || !HasInputAuthority) return;

            if (_vcam == null) RefreshCameraLink();

            bool isAiming = false;

            _pitch = Mathf.Clamp(_pitch, BottomClamp, TopClamp);

            UpdateTargetState(isAiming);

            if (AlignPlayerWithCamera && _hub != null)
            {
                Quaternion targetBodyRot = Quaternion.Euler(0f, _yaw, 0f);
                _hub.transform.rotation = Quaternion.Slerp(_hub.transform.rotation, targetBodyRot, Time.deltaTime * PlayerRotationSpeed);
            }

            if (_vcam != null)
            {
                float targetFOV = isAiming ? AimFOV : NormalFOV;
                _vcam.Lens.FieldOfView = Mathf.Lerp(_vcam.Lens.FieldOfView, targetFOV, Time.deltaTime * TransitionSpeed);
            }
        }

        private void UpdateTargetState(bool isAiming)
        {
            if (_hub == null) return;

            if (_target == null) _target = transform.Find("PlayerTarget")?.gameObject ?? transform.Find("Target")?.gameObject ?? gameObject;

            Transform lookPoint = _hub.CameraLookAtPoint ?? _hub.HeadPoint;

            Vector3 basePosition = (lookPoint != null)
                ? lookPoint.position
                : _hub.transform.position + Vector3.up * 1.6f;

            float minY = _hub.transform.position.y + MinLookAtHeight;
            if (basePosition.y < minY) basePosition.y = minY;

            Quaternion cameraRotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
            _target.transform.rotation = cameraRotation;

            Vector3 targetOffset = isAiming ? AimShoulderOffset : DefaultShoulderOffset;
            targetOffset.x *= _shoulderSide;

            _currentOffset = Vector3.Lerp(_currentOffset, targetOffset, Time.deltaTime * TransitionSpeed);

            // Posicionamiento final del objetivo
            Vector3 finalTargetPos = basePosition + (cameraRotation * _currentOffset);
            _target.transform.position = finalTargetPos;

            // Dibuja una línea roja en la ventana Scene desde la cabeza hacia el objetivo de la cámara
            Debug.DrawLine(basePosition, finalTargetPos, Color.red);
        }

        private void RefreshCameraLink()
        {
            if (_hub == null) return;

            _brain = GetComponentInChildren<CinemachineBrain>(true);
            _vcam = GetComponentInChildren<CinemachineCamera>(true);

            if (_vcam != null)
            {
                _vcam.enabled = HasInputAuthority;

                if (_target == null) UpdateTargetState(false);

                _vcam.Follow = _target.transform;
                _vcam.LookAt = null;

                var cam = GetComponentInChildren<Camera>(true);
                if (cam != null && HasInputAuthority) cam.tag = "MainCamera";
            }
        }

        public void ResetCameraRotation(float targetYaw)
        {
            _yaw = targetYaw;
            _pitch = 0f;
        }

        public void RefreshFunctionalComponents() => RefreshCameraLink();
    }
}