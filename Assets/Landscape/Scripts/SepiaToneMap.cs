// SepiaTonemap.cs
using UnityEngine;

#if UNITY_POST_PROCESSING_STACK_V2
using UnityEngine.Rendering.PostProcessing;
#elif UNITY_PIPELINE_URP || UNITY_PIPELINE_HDRP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

[ExecuteAlways]
public class SepiaTonemap : MonoBehaviour
{
    [Range(-180f, 180f)] public float hueShift = -8f;
    [Range(-100f, 100f)] public float saturation = -25f;
    [Range(-100f, 100f)] public float contraste = 15f;
    [ColorUsage(false)] public Color tinte = new Color(0.9f, 0.55f, 0.3f);

#if UNITY_PIPELINE_URP || UNITY_PIPELINE_HDRP
    Volume _vol;

    void OnEnable()
    {
        _vol = gameObject.GetComponent<Volume>() ?? gameObject.AddComponent<Volume>();
        _vol.isGlobal = true;
        _vol.priority = 10;

        if (_vol.profile == null)
            _vol.profile = ScriptableObject.CreateInstance<VolumeProfile>();

        AplicarURP();
    }

    void OnValidate() => AplicarURP();

    void AplicarURP()
    {
        if (_vol?.profile == null) return;

        if (!_vol.profile.TryGet<ColorAdjustments>(out var ca))
            ca = _vol.profile.Add<ColorAdjustments>(true);

        ca.active           = true;
        ca.hueShift.value   = hueShift;
        ca.saturation.value = saturation;
        ca.contrast.value   = contraste;
        ca.colorFilter.value = tinte;
        ca.colorFilter.overrideState = true;
    }
#else
    // Fallback Built-in: ajuste por RenderSettings
    void OnEnable() => AplicarBuiltin();
    void OnValidate() => AplicarBuiltin();

    void AplicarBuiltin()
    {
        // Sin post-process stack, al menos llevamos el skybox a tono sepia
        RenderSettings.skybox = null;
        Camera.main.backgroundColor = new Color(0.06f, 0.02f, 0.01f);
        Camera.main.clearFlags = CameraClearFlags.SolidColor;
    }
#endif
}