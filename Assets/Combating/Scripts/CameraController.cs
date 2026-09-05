using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for Cinemachine Camera rotation and target positioning.
    /// Acts on the target object which follows the player's head.
    /// </summary>
    public class CameraController : NetworkBehaviour
    {
        [Header("Cinemachine")]
        public GameObject CinemachineCameraTarget;
        public float TopClamp = 70.0f;
        public float BottomClamp = -30.0f;
        public float CameraAngleOverride = 0.0f;
        public bool LockCameraPosition = false;
        public Vector2 LookSensitivity = new Vector2(7.5f, 5.0f);

        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;
        private PlayerController _hub;
        private const float _threshold = 0.01f;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;

        private void Awake()
        {
            _hub = GetComponentInParent<PlayerController>();
        }

        private void Start()
        {
            if (CinemachineCameraTarget != null)
                _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;

            if (IsOwner || !IsNetworkActive)
            {
                SetupCamera();
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                SetupCamera();
            }
            else
            {
                // On remote clients, ensure we don't have a VCam fighting for control
                var root = transform.root.gameObject;
                var vcam = root.GetComponentInChildren<CinemachineCamera>(true);
                if (vcam != null && vcam.transform.IsChildOf(root.transform)) vcam.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (!CanExecuteLocalLogic || _hub == null) return;
            UpdateCameraTargetPosition();
            CameraRotation();
        }

        private void UpdateCameraTargetPosition()
        {
            if (CinemachineCameraTarget == null || _hub.animator == null) return;

            Transform bone = _hub.animator.GetBoneTransform(HumanBodyBones.Head) ??
                             _hub.animator.GetBoneTransform(HumanBodyBones.Neck) ??
                             _hub.animator.transform.Find("Head");

            if (bone != null)
            {
                CinemachineCameraTarget.transform.position = bone.position + Vector3.up * 0.4f;
            }
        }

        private void CameraRotation()
        {
            if (_hub == null) _hub = GetComponentInParent<PlayerController>();
            if (_hub == null) return;

            // Asegurar que tenemos el target del prefab padre
            if (CinemachineCameraTarget == null)
            {
                Transform target = _hub.transform.Find("PlayerTarget");
                if (target != null) CinemachineCameraTarget = target.gameObject;
            }

            if (_hub.look.sqrMagnitude >= _threshold && !LockCameraPosition)
            {
                float mouseSpeedFactor = 0.5f;

                _cinemachineTargetYaw += _hub.look.x * mouseSpeedFactor * LookSensitivity.x;
                _cinemachineTargetPitch -= _hub.look.y * mouseSpeedFactor * LookSensitivity.y;
            }

            _cinemachineTargetYaw = ClampAngle(_cinemachineTargetYaw, float.MinValue, float.MaxValue);
            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

            if (CinemachineCameraTarget != null)
            {
                CinemachineCameraTarget.transform.rotation = Quaternion.Euler(
                    _cinemachineTargetPitch + CameraAngleOverride,
                    _cinemachineTargetYaw,
                    0.0f);
            }
        }

        public void RefreshFunctionalComponents()
        {
            SetupCamera();
        }

        private void SetupCamera()
        {
            if (_hub == null) return;

            GameObject root = _hub.gameObject;
            CinemachineCamera vcam = root.GetComponentInChildren<CinemachineCamera>(true);

            if (vcam != null && CinemachineCameraTarget != null)
            {
                vcam.Follow = CinemachineCameraTarget.transform;
                vcam.LookAt = CinemachineCameraTarget.transform;
                vcam.enabled = true;
                vcam.Priority = 100;
            }
        }

        public void ResetCameraRotation(float targetYaw)
        {
            _cinemachineTargetYaw = targetYaw;
            _cinemachineTargetPitch = 0f;
            if (CinemachineCameraTarget != null)
                CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_cinemachineTargetPitch, _cinemachineTargetYaw, 0f);
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }
    }
}
