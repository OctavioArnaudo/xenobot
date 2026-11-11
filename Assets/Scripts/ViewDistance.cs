// Assets/Scripts (fuera de Editor). Menu: Tools/Perf/Crear ViewDistance (lo pone en la escena) y Tools/Perf/Optimizar URP.
// Mueve el slider "Distancia" en el Inspector, en Play o fuera de Play. Ajusta far clip, arboles/detalle del Terrain y niebla.
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class ViewDistance : MonoBehaviour
{
    [Range(50f, 2000f)] public float distancia = 400f;

    void OnEnable() { RenderPipelineManager.beginCameraRendering += Pre; Camera.onPreCull += PreBuiltin; Apply(); }
    void OnDisable() { RenderPipelineManager.beginCameraRendering -= Pre; Camera.onPreCull -= PreBuiltin; }
    void OnValidate() => Apply();

    // Se fija justo antes de renderizar CADA camara de juego: gana sobre Cinemachine u otros scripts que reescriben el far clip.
    void Pre(ScriptableRenderContext c, Camera cam) => PreBuiltin(cam);
    void PreBuiltin(Camera cam) { if (cam.cameraType == CameraType.Game) cam.farClipPlane = distancia; }

    void Apply()
    {
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