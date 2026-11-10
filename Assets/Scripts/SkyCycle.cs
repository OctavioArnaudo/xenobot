// Poner en un GameObject vacio (junto con NightSky.shader, ambos en Assets, fuera de carpetas Editor).
// Alterna muy lento entre tu skybox actual (dia) y una noche azul cerrada con estrellas brillantes.
// De noche el skybox del dia se REEMPLAZA por el de noche (el dia deja de dibujarse y de iluminar el ambiente).
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class SkyCycle : MonoBehaviour
{
    public Shader shader;
    public float period = 1200f;   // segundos de un ciclo completo dia -> noche -> dia
    public Light sun;              // vacio = RenderSettings.sun o la primera direccional
    public Material[] unlit;       // materiales sin luz que se oscurecen de noche (humo)

    const float NightSun = 0.03f, NightAmbient = 0.15f, NightReflection = 0.1f;
    static readonly Color Moon = new Color(.45f, .55f, 1f), FogNight = new Color(.01f, .02f, .05f), UnlitNight = new Color(.12f, .14f, .25f, 1f);

    bool logged;
    Camera[] cams = new Camera[16];
    Material mat, nightSky, daySky; bool isNight; Transform sky; Renderer rend; Camera cam; float next;
    float sunI, ambI, refI; Color sunC, fogC; Color[] unlitC;
    UnityEngine.Rendering.SphericalHarmonicsL2 held; bool has; UnityEngine.Rendering.AmbientMode amb0;   // ultimo ambiente del dia
    static readonly Vector3[] Dirs = { Vector3.up, Vector3.forward, Vector3.down };
    static readonly Color[] Amb = new Color[3];

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
        ambI = RenderSettings.ambientIntensity; refI = RenderSettings.reflectionIntensity; fogC = RenderSettings.fogColor; amb0 = RenderSettings.ambientMode;
        if (!daySky) Debug.LogWarning("[SkyCycle] No habia skybox de dia al iniciar: se tomara el primero que aparezca (escena creada por codigo).");
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

        // Escenas creadas por codigo: el skybox y el sol pueden aparecer DESPUES de Start. Se capturan cuando existen.
        if (!daySky && !isNight && RenderSettings.skybox && RenderSettings.skybox != nightSky) daySky = RenderSettings.skybox;
        if (!sun && t < 0.001f) { sun = RenderSettings.sun; if (sun) { sunI = sun.intensity; sunC = sun.color; } }
        if (!night && daySky && RenderSettings.skybox != daySky) RenderSettings.skybox = daySky;   // de dia siempre el cielo de dia

        // Toda camara de juego (no overlay, sin RenderTexture) debe dibujar el skybox; cam = la que se usa para posicionar la esfera.
        if (Camera.allCamerasCount > cams.Length) cams = new Camera[Camera.allCamerasCount];
        int cn = Camera.GetAllCameras(cams);
        for (int i = 0; i < cn; i++)
        {
            var c = cams[i];
            if (c.targetTexture || (c.TryGetComponent(out UniversalAdditionalCameraData d) && d.renderType == CameraRenderType.Overlay)) continue;
            if (!cam || !cam.isActiveAndEnabled) cam = c;
            if (!night && RenderSettings.skybox && c.clearFlags != CameraClearFlags.Skybox) c.clearFlags = CameraClearFlags.Skybox;
        }
        if (!logged && Time.time > 3f)
        {
            logged = true;
            var sk = RenderSettings.skybox;
            Debug.Log("[SkyCycle] skybox=" + (sk ? sk.name + " (shader " + sk.shader.name + ", soportado=" + sk.shader.isSupported + ")" : "NINGUNO") + ", camara=" + (cam ? cam.clearFlags.ToString() : "n/a") + ", sol=" + (sun ? sun.intensity.ToString("F2") : "n/a"));
        }
        if (!night && t > 0.8f) { held = RenderSettings.ambientProbe; has = true; }   // guarda el ambiente del final del dia
        if (night != isNight)   // cambio real de skybox (el solape ya cubre el cielo, no se nota)
        {
            isNight = night;
            RenderSettings.skybox = night ? nightSky : daySky;
            if (night && has && amb0 == UnityEngine.Rendering.AmbientMode.Skybox)   // la noche ilumina IGUAL que el punto mas bajo del dia
            {
                held.Evaluate(Dirs, Amb);
                bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = lin ? Amb[0].gamma : Amb[0];
                RenderSettings.ambientEquatorColor = lin ? Amb[1].gamma : Amb[1];
                RenderSettings.ambientGroundColor = lin ? Amb[2].gamma : Amb[2];
            }
            else RenderSettings.ambientMode = amb0;
            DynamicGI.UpdateEnvironment();
        }
        mat.SetFloat("_Blend", t);
        rend.enabled = t > 0.002f && !night;   // de dia y de noche cerrada: costo cero
        if (sun) { sun.intensity = Mathf.Lerp(sunI, sunI * NightSun, t); sun.color = Color.Lerp(sunC, Moon, t); }
        RenderSettings.ambientIntensity = Mathf.Lerp(ambI, ambI * NightAmbient, t);   // continuo: sin salto al cambiar de skybox
        RenderSettings.reflectionIntensity = Mathf.Lerp(refI, refI * NightReflection, t);
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
        if (isNight) { RenderSettings.skybox = daySky; RenderSettings.ambientMode = amb0; }
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