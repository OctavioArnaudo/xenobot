using UnityEngine;

#if UNITY_POST_PROCESSING_STACK_V2
using UnityEngine.Rendering.PostProcessing;
#elif UNITY_PIPELINE_URP || UNITY_PIPELINE_HDRP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace Runtime.Scripts.Sepia
{
    [ExecuteAlways]
    public class GradingController : MonoBehaviour
    {
        [Range(-180f, 180f)] public float hueShift = -8f;
        [Range(-100f, 100f)] public float saturation = -25f;
        [Range(-100f, 100f)] public float contrast = 15f;
        [ColorUsage(false)] public Color tint = new Color(0.9f, 0.55f, 0.3f);

#if UNITY_PIPELINE_URP || UNITY_PIPELINE_HDRP
        Volume _volume;

        void OnEnable()
        {
            EnsureVolume();
            ApplyURP();
        }

        void OnValidate()
        {
            EnsureVolume();
            ApplyURP();
        }

        void EnsureVolume()
        {
            if (_volume == null)
                _volume = gameObject.GetComponent<Volume>() ?? gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10;

            if (_volume.profile == null)
                _volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        void ApplyURP()
        {
            if (_volume?.profile == null) return;

            if (!_volume.profile.TryGet<ColorAdjustments>(out var ca))
                ca = _volume.profile.Add<ColorAdjustments>(true);

            ca.active = true;
            ca.hueShift.value = hueShift;
            ca.saturation.value = saturation;
            ca.contrast.value = contrast;
            ca.colorFilter.value = tint;
            ca.colorFilter.overrideState = true;
        }
#else
        void OnEnable() => ApplyBuiltin();
        void OnValidate() => ApplyBuiltin();

        void ApplyBuiltin()
        {
            RenderSettings.skybox = null;
            var cam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                cam.backgroundColor = new Color(0.06f, 0.02f, 0.01f);
                cam.clearFlags = CameraClearFlags.SolidColor;
            }
        }
#endif
    }
}