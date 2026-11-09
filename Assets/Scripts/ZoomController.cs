using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

namespace Runtime.Scripts.Zoom
{
    /// <summary>
    /// Zoom con la rueda del mouse: no toca el FOV (PlayerController lo gestiona), solo expone un factor
    /// suave que multiplica la distancia de la camara en vista normal. Attach a cualquier GameObject.
    /// </summary>
    public class ZoomController : NetworkBehaviour
    {
        public static float Factor { get; private set; } = 1f;

        public float minFactor = 0.7f;
        public float maxFactor = 1.3f;
        [Tooltip("Cambio del factor por cada click de rueda.")] public float step = 0.1f;
        public float smoothSpeed = 10f;

        float _target;

        void Awake() => Factor = _target = minFactor;

        void Update()
        {
            float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
            if (scroll != 0f) _target = Mathf.Clamp(_target - Mathf.Sign(scroll) * step, minFactor, maxFactor);

            if (Mathf.Abs(Factor - _target) < 0.0005f) { Factor = _target; return; }
            Factor = Mathf.Lerp(Factor, _target, 1f - Mathf.Exp(-smoothSpeed * Time.unscaledDeltaTime));
        }
    }
}