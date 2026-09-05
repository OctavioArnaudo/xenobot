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

            if (_vcam == null) RefreshCameraLink();

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

            // _target is now maintained by OnRefreshModule() via Hub.cameraTarget
            if (_target == null) _target = _hub.gameObject;

            // Priority: HeadPoint from active model
            Transform followBone = null;
            if (_hub.activeModel != null)
            {
                followBone = _hub.activeModel.headPoint;
            }

            if (followBone == null && _hub.animator != null)
            {
                followBone = _hub.animator.GetBoneTransform(HumanBodyBones.Head);
            }

            if (followBone != null)
            {
                _target.transform.position = followBone.position;
            }
            else
            {
                _target.transform.position = _hub.transform.position + Vector3.up * 1.5f;
            }
        }

        private void RefreshCameraLink()
        {
            if (_hub == null) return;
            _vcam = _hub.GetComponentInChildren<CinemachineCamera>(true);
            if (_vcam != null)
            {
                _vcam.enabled = HasInputAuthority;
                if (_target != null)
                {
                    _vcam.Follow = _target.transform;
                    _vcam.LookAt = _target.transform;
                }
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
