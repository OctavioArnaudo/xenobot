using UnityEngine;
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

        public float Yaw => _yaw;
        public float Pitch => _pitch;

        private float _yaw;
        private float _pitch;
        private ModularController _hub;
        private GameObject _target;
        private CinemachineCamera _vcam;

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

            if (_hub is Testing.Scripts.PlayerController playerHub)
            {
                if (playerHub.look.sqrMagnitude > 0.001f)
                {
                    _yaw += playerHub.look.x * LookSensitivity.x;
                    _pitch -= playerHub.look.y * LookSensitivity.y;
                }
            }

            _pitch = Mathf.Clamp(_pitch, BottomClamp, TopClamp);

            if (_target != null)
            {
                _target.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
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
