using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class CameraController : NetworkBehaviour, IPlayerModule
    {
        [Header("Settings")]
        public Vector2 LookSensitivity = new Vector2(1.0f, 0.8f);
        public float TopClamp = 85.0f;
        public float BottomClamp = -60.0f;
        public float MinLookAtHeight = 0.5f;

        public float Yaw => _yaw;
        public float Pitch => _pitch;

        private float _yaw;
        private float _pitch;
        private PlayerController _hub;
        private GameObject _target;
        private CinemachineCamera _vcam;

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

        private void Start()
        {
            if (_hub == null) _hub = PlayerController.LocalInstance;
            if (_hub != null) _yaw = _hub.transform.eulerAngles.y;
            RefreshCameraLink();
        }

        private void LateUpdate()
        {
            if (_hub == null) _hub = PlayerController.LocalInstance;
            if (_hub == null || !HasInputAuthority) return;

            // 1. Ubicar el Target (Seguimiento de posición, NO de rotación)
            UpdateTargetState();

            if (_vcam == null || _vcam.LookAt == null) RefreshCameraLink();

            // 2. Rotación con el Mouse
            if (_hub.look.sqrMagnitude > 0.001f)
            {
                _yaw += _hub.look.x * LookSensitivity.x;
                _pitch -= _hub.look.y * LookSensitivity.y;
            }

            _pitch = Mathf.Clamp(_pitch, BottomClamp, TopClamp);

            if (_target != null)
            {
                // USAMOS WORLD ROTATION (Absoluta)
                // Esto ignora si el padre (Player) rota con WASD
                _target.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
            }
        }

        private void UpdateTargetState()
        {
            if (_hub == null) return;

            // Buscamos el target modular interno
            if (_target == null) _target = transform.Find("PlayerTarget")?.gameObject ?? gameObject;

            Transform lookPoint = _hub.CameraLookAtPoint ?? _hub.HeadPoint;

            if (lookPoint != null)
            {
                // SEGUIMIENTO DE POSICIÓN PURO:
                // El target modular se mueve a la cabeza, pero MANTIENE su propia rotación
                _target.transform.position = lookPoint.position;
            }
            else
            {
                _target.transform.position = _hub.transform.position + Vector3.up * 1.6f;
            }

            // SEGURIDAD: Nunca mirar al suelo (0,0,0) relativo al jugador
            float minY = _hub.transform.position.y + MinLookAtHeight;
            if (_target.transform.position.y < minY)
            {
                Vector3 safePos = _target.transform.position;
                safePos.y = minY;
                _target.transform.position = safePos;
            }

            // La rotación del target es la que manda el ratón, independiente del robot
            _target.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
        }

        private void RefreshCameraLink()
        {
            if (_hub == null) return;

            _vcam = GetComponentInChildren<CinemachineCamera>(true);

            if (_vcam != null)
            {
                _vcam.enabled = HasInputAuthority;

                // Forzamos a Cinemachine a mirar SIEMPRE al target modular interno
                // Este target es el que movemos a la posición del hueso en UpdateTargetState
                if (_target == null) UpdateTargetState();

                _vcam.Follow = _target.transform;
                _vcam.LookAt = _target.transform;

                var cam = GetComponentInChildren<Camera>(true);
                if (cam != null) cam.tag = "MainCamera";
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
