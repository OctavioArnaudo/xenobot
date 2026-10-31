using UnityEngine;
using Unity.Netcode;

namespace Runtime.Scripts.Sepia
{
    [ExecuteAlways]
    public class LightingController : NetworkBehaviour
    {
        [ColorUsage(false)] public Color ambientLight = new Color(0.18f, 0.08f, 0.05f);
        [ColorUsage(false)] public Color directionalLight = new Color(0.55f, 0.18f, 0.08f);
        [ColorUsage(false)] public Color fogColor = new Color(0.12f, 0.04f, 0.02f);

        [Range(0f, 0.05f)] public float fogDensity = 0.012f;
        [Range(0f, 1f)] public float lightIntensity = 0.6f;

        Light _light;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        void Apply()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambientLight;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;

            if (_light == null) _light = FindFirstObjectByType<Light>();
            if (_light != null)
            {
                _light.color = directionalLight;
                _light.intensity = lightIntensity;
            }

            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l != null && l.type != LightType.Directional) l.shadows = LightShadows.None;
            }
        }
    }
}