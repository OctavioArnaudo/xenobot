// SepiaAmbiente.cs
using UnityEngine;

[ExecuteAlways]
public class SepiaAmbiente : MonoBehaviour
{
    [ColorUsage(false)] public Color luzAmbiente = new Color(0.18f, 0.08f, 0.05f);
    [ColorUsage(false)] public Color luzDireccional = new Color(0.55f, 0.18f, 0.08f);
    [ColorUsage(false)] public Color colorNiebla = new Color(0.12f, 0.04f, 0.02f);

    [Range(0f, 0.05f)] public float densidadNiebla = 0.012f;
    [Range(0f, 1f)] public float intensidadLuz = 0.6f;

    Light _luz;

    void OnEnable() => Aplicar();
    void OnValidate() => Aplicar();

    void Aplicar()
    {
        // Ambiente
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = luzAmbiente;

        // Niebla
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = colorNiebla;
        RenderSettings.fogDensity = densidadNiebla;

        // Luz direccional (si existe en escena)
        if (_luz == null) _luz = FindObjectOfType<Light>();
        if (_luz != null)
        {
            _luz.color = luzDireccional;
            _luz.intensity = intensidadLuz;
        }
    }
}