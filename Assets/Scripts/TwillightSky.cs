using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Poner en la Directional Light. Configura sol, ambiente y niebla una sola vez: sin Update, coste cero en juego.</summary>
[ExecuteAlways, RequireComponent(typeof(Light))]
public class TwilightSky : MonoBehaviour
{
    public Material sky;
    [Range(-5, 25)] public float elevation = 5f;
    [Range(0, 360)] public float azimuth = 200f;
    [Range(0, 2)] public float intensity = 0.8f;
    [Range(0, 0.02f)] public float fogAmount = 0.003f;

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    void Apply()
    {
        if (sky == null) return;
        var l = GetComponent<Light>();
        transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);
        l.type = LightType.Directional;
        l.color = sky.GetColor("_SunColor");
        l.intensity = intensity;
        sky.SetVector("_Sun", -transform.forward);

        Color h = sky.GetColor("_Horizon"), m = sky.GetColor("_Mid");
        RenderSettings.sun = l;
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = m;
        RenderSettings.ambientEquatorColor = h * 0.6f;
        RenderSettings.ambientGroundColor = sky.GetColor("_Ground");
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = fogAmount;
        RenderSettings.fogColor = Color.Lerp(h, m, 0.5f) * 0.7f;
    }
}