using UnityEngine;

/// <summary>
/// Attach to your Directional Light (the sun).
/// Rotates it to simulate day/night and exposes current game time.
/// </summary>
public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    [Header("Time")]
    [Tooltip("Duration of a full day in real seconds")]
    public float dayDurationSeconds = 120f;

    [Tooltip("Starting hour (0–24)")]
    [Range(0f, 24f)]
    public float startHour = 8f;

    [Header("Sun")]
    [Tooltip("Max intensity at noon")]
    public float maxSunIntensity = 1.2f;

    [Tooltip("Disable if another script (SepaAmbiente) already controls ambient light")]
    public bool controlAmbientLight = false;

    [Tooltip("Ambient light at noon — only used if controlAmbientLight is true")]
    public Color dayAmbient = new Color(0.8f, 0.8f, 0.9f);

    [Tooltip("Ambient light at midnight — only used if controlAmbientLight is true")]
    public Color nightAmbient = new Color(0.05f, 0.05f, 0.15f);

    // 0–1, where 0 = midnight, 0.5 = noon
    public float TimeOfDay { get; private set; }

    // Current hour as float (0–24)
    public float CurrentHour => TimeOfDay * 24f;

    Light _sun;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

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