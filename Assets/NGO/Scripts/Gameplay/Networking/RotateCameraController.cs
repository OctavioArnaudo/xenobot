using UnityEngine;
using Unity.Netcode;
using NGO.Gameplay.Base;
using StarterAssets;
using UnityEngine.InputSystem;
using System.Linq;

namespace NGO.Gameplay.Networking
{
    public class RotateCameraController : PlayerActionController
    {
        [Header("Camera Settings")]
        public GameObject CinemachineCameraTarget;
        public float TopClamp = 70.0f;
        public float BottomClamp = -30.0f;
        public float CameraAngleOverride = 0.0f;
        public bool LockCameraPosition = false;
        public Vector2 LookSensitivity = new Vector2(7.5f, 5.0f);

        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;
        private const float _threshold = 0.01f;

        private StarterAssetsInputs _input;
#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif

        public override void Initialize(Unity.Netcode.NetworkObject root)
        {
            base.Initialize(root);

            _input = root.GetComponentInChildren<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
            _playerInput = root.GetComponentInChildren<PlayerInput>();
#endif
            if (CinemachineCameraTarget == null)
            {
                var target = playerRoot.transform.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "PlayerCameraRoot");
                if (target != null) CinemachineCameraTarget = target.gameObject;
            }

            if (CinemachineCameraTarget != null)
                _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;
        }

        public override void OnActionTriggered() { }

        private void LateUpdate()
        {
            if (!IsOwner || CinemachineCameraTarget == null || _input == null) return;

            CameraRotation();
        }

        private void CameraRotation()
        {
            if (_input.look.sqrMagnitude >= _threshold && !LockCameraPosition)
            {
                bool isMouse = true;
#if ENABLE_INPUT_SYSTEM
                if (_playerInput != null) isMouse = _playerInput.currentControlScheme == "KeyboardMouse";
#endif
                float deltaTimeMultiplier = isMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetYaw += _input.look.x * deltaTimeMultiplier * LookSensitivity.x;
                _cinemachineTargetPitch += _input.look.y * deltaTimeMultiplier * LookSensitivity.y;
            }

            _cinemachineTargetYaw = ClampAngle(_cinemachineTargetYaw, float.MinValue, float.MaxValue);
            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

            CinemachineCameraTarget.transform.rotation = Quaternion.Euler(
                _cinemachineTargetPitch + CameraAngleOverride,
                _cinemachineTargetYaw,
                0.0f
            );
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }
    }
}
