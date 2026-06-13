using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// Zoom con rueda del mouse via FOV de la CinemachineCamera activa.
/// Attach a cualquier GameObject en la escena.
/// </summary>
public class CameraZoom : MonoBehaviour
{
    [Header("FOV")]
    public float minFov = 20f;
    public float maxFov = 80f;
    public float sensitivity = 5f;
    public float smoothSpeed = 8f;

    CinemachineCamera _vcam;
    float _targetFov;

    void Start()
    {
        _vcam = FindFirstObjectByType<CinemachineCamera>();
        if (_vcam == null) { Debug.LogWarning("[CameraZoom] No CinemachineCamera found."); return; }
        _targetFov = _vcam.Lens.FieldOfView;
    }

    void Update()
    {
        if (_vcam == null) return;

        float scroll = Mouse.current?.scroll.ReadValue().y ?? 0f;
        if (scroll != 0f)
            _targetFov = Mathf.Clamp(_targetFov - scroll * sensitivity * 0.01f, minFov, maxFov);

        var lens = _vcam.Lens;
        lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, _targetFov, Time.deltaTime * smoothSpeed);
        _vcam.Lens = lens;
    }
}