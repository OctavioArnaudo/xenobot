// Assets/Scripts (fuera de Editor). Menu: Tools/Perf/Crear ViewDistance (lo pone en la escena) y Tools/Perf/Optimizar URP.
// Mueve el slider "Distancia" en el Inspector, en Play o fuera de Play. Ajusta far clip, arboles/detalle del Terrain y niebla.
// "Escala" = Render Scale de URP: reduce SOLO la resolucion 3D. Los Canvas en Screen Space - Overlay se dibujan a resolucion nativa (HUD y menus nitidos);
// los Canvas en modo Camera/World SI se escalan (se avisa en consola). "Dinamica": baja/sube la escala sola para sostener fpsObjetivo (Escala = maximo).
// "Resolucion Bosque" = regula en el Editor los modelos de ForestFast generados con el generador 6 (ver ForestRes al final del archivo).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
public class ViewDistance : MonoBehaviour
{
    // Distancias (altura relativa en pantalla) de los 3 LOD de los modelos de ForestFast. Las usan el generador 6 y ForestRes.
    public const float ForestLod0 = .30f, ForestLod1 = .12f, ForestCull = .001f;

    [Range(50f, 2000f)] public float distancia = 400f;
    [Range(.25f, 1f)] public float escala = 1f;
    public bool dinamica; [Range(30, 144)] public int fpsObjetivo = 60;

    [Header("Bosque ForestFast (generador 6)")]
    [Tooltip("1 = LOD como se generaron. Menos = pasa a las mallas mas simples a menor distancia. En el minimo, todo el bosque usa siempre la malla mas barata.")]
    [Range(.02f, 1f)] public float resolucionBosque = 1f;

    float orig = 1f, cur = 1f, acc, okFor; int frames;

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += Pre; Camera.onPreCull += PreBuiltin;
        var a = UniversalRenderPipeline.asset; if (a) orig = a.renderScale;
        cur = escala; Apply();
        if (Application.isPlaying)
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.renderMode != RenderMode.ScreenSpaceOverlay) { Debug.LogWarning($"[ViewDistance] Canvas '{c.name}' no es Screen Space - Overlay: se vera borroso con Escala < 1.", c); }
    }
    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= Pre; Camera.onPreCull -= PreBuiltin;
        var a = UniversalRenderPipeline.asset; if (a) a.renderScale = orig;
    }

    void Update()
    {
        if (!dinamica || !Application.isPlaying) return;
        acc += Time.unscaledDeltaTime; frames++;
        if (acc < 1f) return;
        float fps = frames / acc; acc = 0f; frames = 0;
        if (fps < fpsObjetivo * .9f) { okFor = 0f; cur -= .05f; }      // baja rapido
        else if (fps >= fpsObjetivo * .97f && ++okFor >= 5f) { okFor = 0f; cur += .05f; }   // sube tras 5 s estables
        cur = Mathf.Clamp(cur, .25f, escala);
        Scale(cur);
    }

    static void Scale(float v) { var a = UniversalRenderPipeline.asset; if (a && !Mathf.Approximately(a.renderScale, v)) a.renderScale = v; }

    void OnValidate()
    {
        Apply();
#if UNITY_EDITOR
        ForestRes.Request(resolucionBosque);
#endif
    }

    // Se fija justo antes de renderizar CADA camara de juego: gana sobre Cinemachine u otros scripts que reescriben el far clip.
    void Pre(ScriptableRenderContext c, Camera cam) => PreBuiltin(cam);
    void PreBuiltin(Camera cam) { if (cam.cameraType == CameraType.Game) cam.farClipPlane = distancia; }

    void Apply()
    {
        cur = Mathf.Min(cur, escala); Scale(dinamica && Application.isPlaying ? cur : escala);
        float d = distancia;
        foreach (var t in Terrain.activeTerrains) { t.treeBillboardDistance = d * .5f; t.detailObjectDistance = Mathf.Min(d * .25f, 120f); t.basemapDistance = d * .5f; }
        if (RenderSettings.fog) { RenderSettings.fogEndDistance = d; RenderSettings.fogStartDistance = d * .5f; }
    }
}

#if UNITY_EDITOR
// Regula los modelos de ForestFast ya generados: reescribe las distancias de LOD de sus prefabs (ForestFast/Static) y refresca el terreno.
// Resolucion 1 = como se generaron. Menor = los LOD baratos entran a menor distancia (hasta usar siempre el ultimo). Solo afecta al bosque de ForestFast.
public static class ForestRes
{
    const string Key = "ForestRes.applied";
    static float want, stamp; static bool hooked;

    // Llamado desde OnValidate: espera 0.4 s sin cambios (mientras arrastras la barra) antes de tocar los prefabs.
    public static void Request(float res)
    {
        if (Mathf.Approximately(UnityEditor.SessionState.GetFloat(Key, -1f), res)) return;
        want = res; stamp = (float)UnityEditor.EditorApplication.timeSinceStartup;
        if (hooked) return;
        hooked = true; UnityEditor.EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (UnityEditor.EditorApplication.timeSinceStartup - stamp < .4f) return;
        UnityEditor.EditorApplication.update -= Tick; hooked = false;
        Apply(want);
    }

    public static int Apply(float res)
    {
        res = Mathf.Clamp(res, .02f, 1f);
        float m = 1f / res;
        float h0 = Mathf.Min(.99f, ViewDistance.ForestLod0 * m);
        float h1 = Mathf.Min(h0 - .005f, ViewDistance.ForestLod1 * m);
        float h2 = Mathf.Min(h1 - .005f, ViewDistance.ForestCull);

        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        var paths = new System.Collections.Generic.HashSet<string>();
        foreach (var t in ts)
            foreach (var p in t.terrainData.treePrototypes)
            {
                if (!p.prefab) continue;
                string ap = UnityEditor.AssetDatabase.GetAssetPath(p.prefab);
                if (ap.Contains("/ForestFast/Static/")) paths.Add(ap);
            }

        int n = 0;
        foreach (var ap in paths)
        {
            var root = UnityEditor.PrefabUtility.LoadPrefabContents(ap);
            var lg = root.GetComponentInChildren<LODGroup>(true);
            if (lg != null && lg.lodCount == 3)
            {
                var l = lg.GetLODs();
                l[0].screenRelativeTransitionHeight = h0;
                l[1].screenRelativeTransitionHeight = h1;
                l[2].screenRelativeTransitionHeight = h2;
                lg.SetLODs(l);
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, ap);
                n++;
            }
            UnityEditor.PrefabUtility.UnloadPrefabContents(root);
        }
        foreach (var t in ts) { t.terrainData.RefreshPrototypes(); t.Flush(); }
        UnityEditor.SessionState.SetFloat(Key, res);
        Debug.Log($"[ForestRes] Resolucion {res:0.00}: {n} prefabs de ForestFast ajustados (LOD0 hasta {h0:0.000}, LOD1 hasta {h1:0.000}).");
        return n;
    }
}

static class ViewDistanceMenu
{
    [UnityEditor.MenuItem("Tools/Perf/Crear ViewDistance")]
    static void Create()
    {
        var v = Object.FindFirstObjectByType<ViewDistance>();
        if (!v) { v = new GameObject("ViewDistance").AddComponent<ViewDistance>(); UnityEditor.Undo.RegisterCreatedObjectUndo(v.gameObject, "ViewDistance"); }
        UnityEditor.Selection.activeObject = v.gameObject;
    }
}

// Tools/Perf/Optimizar URP: detecta TODOS los pipeline assets en uso (default + cada Quality level) y los deja livianos.
static class UrpOptimizer
{
    [UnityEditor.MenuItem("Tools/Perf/Optimizar URP")]
    static void Run()
    {
        var set = new System.Collections.Generic.HashSet<UnityEngine.Rendering.RenderPipelineAsset>();
        if (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline) set.Add(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline);
        for (int i = 0; i < QualitySettings.names.Length; i++) { var a = QualitySettings.GetRenderPipelineAssetAt(i); if (a) set.Add(a); }
        if (set.Count == 0) { QualitySettings.shadowDistance = 60f; Debug.Log("[Perf] Pipeline Built-in: shadowDistance = 60."); return; }

        foreach (var a in set)
        {
            var so = new UnityEditor.SerializedObject(a);
            int n = 0;
            n += Set(so, "m_ShadowDistance", 60f);                  // sombras solo cerca (lo que mas pesa)
            n += Set(so, "m_ShadowCascadeCount", 2);
            n += Set(so, "m_MainLightShadowmapResolution", 1024);
            n += Set(so, "m_SoftShadowsSupported", false);
            n += Set(so, "m_AdditionalLightShadowsSupported", false);
            n += Set(so, "m_AdditionalLightsPerObjectLimit", 4);
            n += Set(so, "m_MSAA", 1);                              // sin MSAA (usa AA de la camara: SMAA/FXAA)
            n += Set(so, "m_RenderScale", 1f);
            n += Set(so, "m_UseSRPBatcher", true);
            n += Set(so, "m_SupportsDynamicBatching", false);
            so.ApplyModifiedProperties(); UnityEditor.EditorUtility.SetDirty(a);
            Debug.Log($"[Perf] {a.name} ({a.GetType().Name}): {n} ajustes aplicados.", a);
        }
        UnityEditor.AssetDatabase.SaveAssets();
    }

    static int Set(UnityEditor.SerializedObject so, string name, object v)
    {
        var p = so.FindProperty(name); if (p == null) return 0;
        switch (p.propertyType)
        {
            case UnityEditor.SerializedPropertyType.Float: p.floatValue = (float)v; break;
            case UnityEditor.SerializedPropertyType.Boolean: p.boolValue = (bool)v; break;
            default: p.intValue = (int)v; break;
        }
        return 1;
    }
}
#endif