using Runtime.Scripts.Singleton;
using UnityEngine;

namespace Runtime.Scripts.Cycle
{
    public class CycleController : CSingletonMB<CycleController>
    {
        [Header("Time")]
        [Tooltip("Duration of a full day in real seconds")]
        public float dayDurationSeconds = 120f;

        [Tooltip("Starting hour (0-24)")]
        [Range(0f, 24f)]
        public float startHour = 8f;

        [Header("Sun")]
        [Tooltip("Max intensity at noon")]
        public float maxSunIntensity = 1.2f;

        [Tooltip("Disable if another script (LightingController) already controls ambient light")]
        public bool controlAmbientLight = false;

        [Tooltip("Ambient light at noon - only used if controlAmbientLight is true")]
        public Color dayAmbient = new Color(0.8f, 0.8f, 0.9f);

        [Tooltip("Ambient light at midnight - only used if controlAmbientLight is true")]
        public Color nightAmbient = new Color(0.05f, 0.05f, 0.15f);

        public float TimeOfDay { get; private set; }

        public float CurrentHour => TimeOfDay * 24f;

        Light _sun;

        protected override void Awake()
        {
            base.Awake();

            _sun = GetComponent<Light>();
            TimeOfDay = startHour / 24f;
        }

        void Update()
        {
            TimeOfDay = (TimeOfDay + Time.deltaTime / dayDurationSeconds) % 1f;

            float sunAngle = TimeOfDay * 360f - 90f;
            transform.rotation = Quaternion.Euler(sunAngle, -30f, 0f);

            float dayBlend = Mathf.Clamp01(Mathf.Sin(TimeOfDay * Mathf.PI));

            if (_sun != null)
                _sun.intensity = Mathf.Lerp(0f, maxSunIntensity, dayBlend);

            if (controlAmbientLight)
                RenderSettings.ambientLight = Color.Lerp(nightAmbient, dayAmbient, dayBlend);
        }
    }
}