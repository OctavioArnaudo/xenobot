// Assets/Scripts (fuera de Editor). Menu: Tools/Perf/Crear ViewDistance (lo pone en la escena) y Tools/Perf/Optimizar URP.
// Mueve el slider "Distancia" en el Inspector, en Play o fuera de Play. Ajusta far clip, arboles/detalle del Terrain y niebla.
// "Escala" = Render Scale de URP: reduce SOLO la resolucion 3D. Los Canvas en Screen Space - Overlay se dibujan a resolucion nativa (HUD y menus nitidos);
// los Canvas en modo Camera/World SI se escalan (se avisa en consola). "Dinamica": baja/sube la escala sola para sostener fpsObjetivo (Escala = maximo).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
public class ViewDistance : MonoBehaviour
{
    [Range(50f, 2000f)] public float distancia = 400f;
    [Range(.25f, 1f)] public float escala = 1f;
    public bool dinamica; [Range(30, 144)] public int fpsObjetivo = 60;

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
    void OnValidate() => Apply();

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