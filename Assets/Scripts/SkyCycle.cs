// Poner en un GameObject vacio (junto con NightSky.shader, ambos en Assets, fuera de carpetas Editor).
// Alterna muy lento entre tu skybox actual (dia) y una noche azul cerrada con estrellas brillantes.
// De noche el skybox del dia se REEMPLAZA por el de noche (el dia deja de dibujarse y de iluminar el ambiente).
using UnityEngine;

public class SkyCycle : MonoBehaviour
{
    public Shader shader;
    public float period = 1200f;   // segundos de un ciclo completo dia -> noche -> dia
    public Light sun;              // vacio = RenderSettings.sun o la primera direccional
    public Material[] unlit;       // materiales sin luz que se oscurecen de noche (humo)

    const float NightSun = 0.03f, NightAmbient = 0.15f, NightReflection = 0.1f;
    static readonly Color Moon = new Color(.45f, .55f, 1f), FogNight = new Color(.01f, .02f, .05f), UnlitNight = new Color(.12f, .14f, .25f, 1f);

    Material mat, nightSky, daySky; bool isNight; Transform sky; Renderer rend; Camera cam; float next;
    float sunI, ambI, refI; Color sunC, fogC; Color[] unlitC;

    void Start()
    {
        if (!shader) shader = Shader.Find("Sky/NightOverlay");
        mat = new Material(shader);
        nightSky = new Material(shader);
        nightSky.SetFloat("_Cull", 0f);
        nightSky.SetFloat("_Blend", 1f);
        daySky = RenderSettings.skybox;
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.hideFlags = HideFlags.HideAndDontSave;
        sky = go.transform;
        rend = go.GetComponent<Renderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.enabled = false;

        if (!sun) sun = RenderSettings.sun;
        if (!sun) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
        if (sun) { sunI = sun.intensity; sunC = sun.color; }
        ambI = RenderSettings.ambientIntensity; refI = RenderSettings.reflectionIntensity; fogC = RenderSettings.fogColor;
        if (unlit == null) unlit = new Material[0];
        unlitC = new Color[unlit.Length];
        for (int i = 0; i < unlit.Length; i++) unlitC[i] = unlit[i].color;
    }

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + 0.5f;
        float e = 0.5f - 0.5f * Mathf.Cos(Time.time * Mathf.PI * 2f / period);
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((e - 0.25f) * 2f));   // 0 dia, 1 noche: tramos fijos largos y transiciones cortas
        bool night = t > 0.97f;
        if (night != isNight)   // cambio real de skybox (el solape ya cubre el cielo, no se nota)
        {
            isNight = night;
            RenderSettings.skybox = night ? nightSky : daySky;
            DynamicGI.UpdateEnvironment();
        }
        mat.SetFloat("_Blend", t);
        rend.enabled = t > 0.002f && !night;   // de dia y de noche cerrada: costo cero
        if (sun) { sun.intensity = Mathf.Lerp(sunI, sunI * NightSun, t); sun.color = Color.Lerp(sunC, Moon, t); }
        RenderSettings.ambientIntensity = night ? ambI : Mathf.Lerp(ambI, ambI * NightAmbient, t);   // de noche el ambiente ya sale del cielo nocturno
        RenderSettings.reflectionIntensity = night ? refI : Mathf.Lerp(refI, refI * NightReflection, t);
        RenderSettings.fogColor = Color.Lerp(fogC, FogNight, t);
        for (int i = 0; i < unlit.Length; i++) unlit[i].color = Color.Lerp(unlitC[i], UnlitNight, t);
    }

    void LateUpdate()
    {
        if (!rend || !rend.enabled) return;
        if (!cam) cam = Camera.main;
        if (!cam) return;
        sky.position = cam.transform.position;
        sky.localScale = Vector3.one * cam.farClipPlane * 1.9f;   // radio = 95% del far clip
    }

    void OnDestroy()
    {
        if (unlitC != null) for (int i = 0; i < unlit.Length; i++) unlit[i].color = unlitC[i];
        if (isNight) RenderSettings.skybox = daySky;
        if (sky) Destroy(sky.gameObject);
    }

#if UNITY_EDITOR
    void Reset()
    {
        shader = Shader.Find("Sky/NightOverlay");
        var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Prefabs/Bombs/Smoke.mat");
        if (m) unlit = new[] { m };
    }
#endif
}