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
        public Vector3 DefaultShoulderOffset = new Vector3(0.65f, 0.1f, -2.5f); // X: Derecha/Izquierda, Y: Altura, Z: Distancia
        public Vector3 AimShoulderOffset = new Vector3(0.45f, 0.05f, -1.5f);
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
        private float _shoulderSide = 1f; // 1 = Hombro derecho, -1 = Hombro izquierdo
        private ModularController _hub;
        private GameObject _target;
        private CinemachineCamera _vcam;
        private CinemachineThirdPersonFollow _thirdPersonFollow;

        private bool HasInputAuthority => _hub != null && _hub is PlayerController && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || _hub.IsOwner);

        void Awake()
        {
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
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

            UpdateTargetState();

            if (_vcam == null || _vcam.LookAt == null) RefreshCameraLink();

            bool isAiming = false;

            if (_hub is Testing.Scripts.PlayerController playerHub)
            {
                if (playerHub.look.sqrMagnitude > 0.001f)
                {
                    _yaw += playerHub.look.x * LookSensitivity.x;
                    _pitch -= playerHub.look.y * LookSensitivity.y;
                }

                // Detectar si el jugador está apuntando (vía variable 'aim' o clic derecho)
                isAiming = playerHub.aim || (Mouse.current != null && Mouse.current.rightButton.isPressed);

                // Opcional: Cambiar de hombro presionando el botón central del mouse (MMB) o la tecla C
                if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
                {
                    _shoulderSide *= -1f;
                }
            }

            _pitch = Mathf.Clamp(_pitch, BottomClamp, TopClamp);

            if (_target != null)
            {
                _target.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
            }

            // Orientar la rotación del cuerpo del personaje hacia la mirada de la cámara
            if (AlignPlayerWithCamera && _hub != null)
            {
                Quaternion targetBodyRot = Quaternion.Euler(0f, _yaw, 0f);
                _hub.transform.rotation = Quaternion.Slerp(_hub.transform.rotation, targetBodyRot, Time.deltaTime * PlayerRotationSpeed);
            }

            UpdateShoulderCameraState(isAiming);
        }

        private void UpdateShoulderCameraState(bool isAiming)
        {
            if (_vcam == null) return;

            // Transición de FOV (Zoom al apuntar)
            float targetFOV = isAiming ? AimFOV : NormalFOV;
            _vcam.Lens.FieldOfView = Mathf.Lerp(_vcam.Lens.FieldOfView, targetFOV, Time.deltaTime * TransitionSpeed);

            // Ajustar el offset en CinemachineThirdPersonFollow si está configurado en la VCam
            if (_thirdPersonFollow != null)
            {
                Vector3 targetOffset = isAiming ? AimShoulderOffset : DefaultShoulderOffset;
                targetOffset.x *= _shoulderSide; // Invertir si se cambia de hombro

                _thirdPersonFollow.ShoulderOffset = Vector3.Lerp(_thirdPersonFollow.ShoulderOffset, targetOffset, Time.deltaTime * TransitionSpeed);
            }
        }

        private void UpdateTargetState()
        {
            if (_hub == null) return;

            if (_target == null) _target = transform.Find("PlayerTarget")?.gameObject ?? transform.Find("Target")?.gameObject ?? gameObject;

            Transform lookPoint = _hub.CameraLookAtPoint ?? _hub.HeadPoint;

            if (lookPoint != null) _target.transform.position = lookPoint.position;
            else _target.transform.position = _hub.transform.position + Vector3.up * 1.6f;

            float minY = _hub.transform.position.y + MinLookAtHeight;
            if (_target.transform.position.y < minY)
            {
                Vector3 safePos = _target.transform.position;
                safePos.y = minY;
                _target.transform.position = safePos;
            }

            _target.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
        }

        private void RefreshCameraLink()
        {
            if (_hub == null) return;

            _vcam = GetComponentInChildren<CinemachineCamera>(true);

            if (_vcam != null)
            {
                _vcam.enabled = HasInputAuthority;

                if (_target == null) UpdateTargetState();

                _vcam.Follow = _target.transform;
                _vcam.LookAt = _target.transform;

                // Obtener el componente de tercera persona de Cinemachine 3.x
                _thirdPersonFollow = _vcam.GetComponent<CinemachineThirdPersonFollow>();

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