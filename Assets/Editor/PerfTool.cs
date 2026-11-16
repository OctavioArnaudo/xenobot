using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Editor.Scripts.Performance
{

    public static class PerfTool
    {
        const float TreeDist = 1200; // bajar si sigue lento
        const bool TreeColliders = false; // false = los arboles del Terrain no colisionan (ni con camara ni con player)
        const string LTrees = "Trees", LEnemies = "Enemies";

        [MenuItem("Tools/Perf/Apply All")]
        static void All() { Terrains(); Trees(); Scene(); Quality(); Urp(); AssetDatabase.SaveAssets(); }

        static void Terrains()
        {
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                Undo.RecordObject(t, "Perf");
                t.drawInstanced = true;
                t.heightmapPixelError = 40;
                t.basemapDistance = 150;
                t.shadowCastingMode = ShadowCastingMode.Off;
                t.reflectionProbeUsage = ReflectionProbeUsage.Off;
                t.treeDistance = TreeDist;
                t.treeBillboardDistance = 5000;
                t.treeCrossFadeLength = 0;
                t.treeMaximumFullLODCount = 10000;
                t.detailObjectDistance = 30;
                t.detailObjectDensity = 0.3f;
                EditorUtility.SetDirty(t);
            }
        }

        // Arboles: LOD0 corto, LOD1 = octaedro de 8 triangulos, sin culling, sin sombras, instancing
        static void Trees()
        {
            AssetDatabase.DeleteAsset("Assets/TreeLowLOD.asset");
            var done = new HashSet<string>();
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
                foreach (var p in t.terrainData.treePrototypes)
                {
                    var path = p.prefab ? AssetDatabase.GetAssetPath(p.prefab) : null;
                    if (string.IsNullOrEmpty(path) || !done.Add(path)) continue;
                    var root = PrefabUtility.LoadPrefabContents(path);
                    var lg = root.GetComponent<LODGroup>();
                    bool had = lg != null;
                    if (!had) lg = root.AddComponent<LODGroup>();
                    var low = root.transform.Find("LowLOD");
                    var l0 = had ? lg.GetLODs()[0].renderers : root.GetComponentsInChildren<Renderer>();
                    if (l0 != null && l0.Length > 0)
                    {
                        Renderer big = l0[0];
                        var b = l0[0].bounds;
                        foreach (var r in l0)
                        {
                            b.Encapsulate(r.bounds);
                            if (r.bounds.size.sqrMagnitude > big.bounds.size.sqrMagnitude) big = r;
                        }
                        var mp = "Assets/TreeLowLOD_" + root.name + ".asset";
                        var m = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
                        bool fresh = m == null;
                        if (fresh) m = new Mesh { name = "TreeLowLOD" }; else m.Clear();
                        Shape(m, b, root.transform);
                        if (fresh) AssetDatabase.CreateAsset(m, mp); else EditorUtility.SetDirty(m);

                        MeshRenderer mr;
                        if (low) mr = low.GetComponent<MeshRenderer>();
                        else
                        {
                            var go = new GameObject("LowLOD");
                            go.transform.SetParent(root.transform, false);
                            go.AddComponent<MeshFilter>();
                            mr = go.AddComponent<MeshRenderer>();
                        }
                        mr.GetComponent<MeshFilter>().sharedMesh = m;
                        mr.sharedMaterial = big.sharedMaterial;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        mr.receiveShadows = false;
                        mr.lightProbeUsage = LightProbeUsage.Off;
                        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        lg.fadeMode = LODFadeMode.None;
                        lg.SetLODs(new[] { new LOD(0.12f, l0), new LOD(0f, new Renderer[] { mr }) });
                        foreach (var r in root.GetComponentsInChildren<Renderer>())
                            foreach (var mat in r.sharedMaterials)
                                if (mat) { mat.enableInstancing = true; EditorUtility.SetDirty(mat); }
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    PrefabUtility.UnloadPrefabContents(root);
                }
        }

        static void Shape(Mesh m, Bounds b, Transform r)
        {
            Vector3 c = r.InverseTransformPoint(b.center);
            float h = b.size.y, y0 = c.y - b.extents.y;
            float cy = y0 + h * 0.575f, ry = h * 0.425f, rx = b.extents.x * 0.7f, rz = b.extents.z * 0.7f;
            var T = new Vector3(c.x, cy + ry, c.z); var B = new Vector3(c.x, cy - ry, c.z);
            var N = new Vector3(c.x, cy, c.z + rz); var E = new Vector3(c.x + rx, cy, c.z);
            var S = new Vector3(c.x, cy, c.z - rz); var W = new Vector3(c.x - rx, cy, c.z);
            var v = new[] { T, N, E, T, E, S, T, S, W, T, W, N, B, E, N, B, S, E, B, W, S, B, N, W };
            var tr = new int[24];
            for (int i = 0; i < 24; i++) tr[i] = i;
            m.vertices = v; m.triangles = tr;
            m.RecalculateNormals(); m.RecalculateBounds();
        }

        // Escena: sin sombras en todo lo que no es terreno, con instancing
        static void Scene()
        {
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                Undo.RecordObject(r, "Perf");
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                foreach (var mat in r.sharedMaterials)
                    if (mat && mat.shader && !mat.shader.name.Contains("Terrain")) mat.enableInstancing = true;
                EditorUtility.SetDirty(r);
            }
        }

        static void Quality()
        {
            int cur = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.lodBias = 1f;
                QualitySettings.shadowDistance = 40;
                QualitySettings.shadowCascades = 1;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                QualitySettings.antiAliasing = 0;
                QualitySettings.realtimeReflectionProbes = false;
                QualitySettings.softParticles = false;
                QualitySettings.globalTextureMipmapLimit = 1;
            }
            QualitySettings.SetQualityLevel(cur, false);
        }

        static void Urp()
        {
            var set = new HashSet<RenderPipelineAsset> { GraphicsSettings.defaultRenderPipeline };
            for (int i = 0; i < QualitySettings.names.Length; i++) set.Add(QualitySettings.GetRenderPipelineAssetAt(i));
            foreach (var a in set)
            {
                if (!a) continue;
                var so = new SerializedObject(a);
                S(so, "m_RenderScale", 0.7f); S(so, "m_MSAA", 1); S(so, "m_SupportsHDR", false);
                S(so, "m_ShadowDistance", 40f); S(so, "m_MainLightShadowmapResolution", 512);
                S(so, "m_ShadowCascadeCount", 1); S(so, "m_SoftShadowsSupported", false);
                S(so, "m_SupportsCameraDepthTexture", false); S(so, "m_SupportsCameraOpaqueTexture", false);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(a);
            }
        }

        static void S(SerializedObject so, string n, object v)
        {
            var p = so.FindProperty(n);
            if (p == null) return;
            if (v is float f) p.floatValue = f;
            else if (v is int i) p.intValue = i;
            else if (v is bool b) p.boolValue = b;
        }

        // Camara: ignora Trees, Enemies y Player. Enemigos (HealthController sin tag Player) -> capa Enemies
        [MenuItem("Tools/Perf/Camera Collision")]
        static void CamFix()
        {
            int tr = Layer(LTrees), en = Layer(LEnemies);
            if (tr < 0 || en < 0) { Debug.LogError("PerfTool: no hay capas libres"); return; }
            int pl = LayerMask.NameToLayer("Player");
            int mask = ~((1 << tr) | (1 << en) | (pl >= 0 ? 1 << pl : 0));

            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                var tc = t.GetComponent<TerrainCollider>();
                if (tc)
                {
                    var so = new SerializedObject(tc);
                    var p = so.FindProperty("m_EnableTreeColliders");
                    if (p != null) { p.boolValue = TreeColliders; so.ApplyModifiedProperties(); }
                }
                foreach (var pr in t.terrainData.treePrototypes)
                {
                    var path = pr.prefab ? AssetDatabase.GetAssetPath(pr.prefab) : null;
                    if (string.IsNullOrEmpty(path)) continue;
                    SetLayer(pr.prefab, tr);
                }
            }

            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Fix(root, mask, en);

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!asset || !Hit(asset)) continue;
                Fix(asset, mask, en);
            }
            AssetDatabase.SaveAssets();
        }

        static bool Hit(GameObject g)
        {
            foreach (var m in g.GetComponentsInChildren<MonoBehaviour>(true))
                if (m) { var n = m.GetType().Name; if (n == "PlayerController" || n == "HealthController") return true; }
            return false;
        }

        static bool Fix(GameObject root, int mask, int en)
        {
            bool ch = false;
            foreach (var m in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!m) continue;
                var n = m.GetType().Name;
                if (n == "PlayerController")
                {
                    var so = new SerializedObject(m);
                    var p = so.FindProperty("cameraCollision");
                    if (p == null) continue;
                    p.intValue = mask;
                    so.ApplyModifiedProperties();
                    ch = true;
                }
                else if (n == "HealthController" && !IsPlayer(m.transform))
                {
                    SetLayer(m.gameObject, en);
                    ch = true;
                }
            }
            return ch;
        }

        static bool IsPlayer(Transform t)
        {
            for (; t; t = t.parent) if (t.CompareTag("Player")) return true;
            return false;
        }

        static void SetLayer(GameObject g, int l)
        {
            g.layer = l;
            EditorUtility.SetDirty(g);
            foreach (Transform c in g.transform) SetLayer(c.gameObject, l);
        }

        // Devuelve el indice de la capa; la crea si no existe. -1 si no hay hueco.
        static int Layer(string n)
        {
            int i = LayerMask.NameToLayer(n);
            if (i >= 0) return i;
            var so = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/TagManager.asset"));
            var l = so.FindProperty("layers");
            for (i = 8; i < 32; i++)
            {
                var e = l.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(e.stringValue)) continue;
                e.stringValue = n;
                so.ApplyModifiedProperties();
                return i;
            }
            return -1;
        }
    }
}