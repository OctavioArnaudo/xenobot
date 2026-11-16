using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using Runtime.Scripts.Melee;
using Runtime.Scripts.Health;
using Runtime.Scripts.Spawn;
using Runtime.Scripts.Shoot;

namespace Editor.Scripts.Enemy
{
    public static class EnemyManager
    {
        [MenuItem("Tools/Enemy/Fill with Enemies")]
        static void FillWithEnemies()
        {
            var ts = Terrain.activeTerrains;
            if (ts.Length == 0) return;

            var grp = GameObject.Find("EnemiesPrefab") ?? new GameObject("EnemiesPrefab");
            var rnd = new System.Random();

            for (int i = 0; i < 20; i++)
            {
                int variant = rnd.Next(1, 9);
                string renderPath = $"Assets/Prefabs/EnemyRender ({variant}).prefab";
                string prefabPath = $"Assets/Prefabs/EnemyPrefab ({variant}).prefab";

                var renderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(renderPath);
                var logicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                if (renderPrefab == null) continue;

                var t = ts[rnd.Next(ts.Length)];
                Vector3 tp = t.transform.position, sz = t.terrainData.size;
                Vector3 p = new Vector3(tp.x + (float)rnd.NextDouble() * sz.x, 0f, tp.z + (float)rnd.NextDouble() * sz.z);
                p.y = tp.y + t.SampleHeight(p);

                if (NavMesh.SamplePosition(p, out var h, 100f, NavMesh.AllAreas))
                {
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(renderPrefab, grp.transform);
                    g.name = "EnemyPrefab";
                    g.transform.position = h.position;

                    if (logicPrefab != null)
                    {
                        var comps = logicPrefab.GetComponents<Component>();
                        foreach (var comp in comps)
                        {
                            if (comp == null || comp is Transform) continue;
                            UnityEditorInternal.ComponentUtility.CopyComponent(comp);
                            UnityEditorInternal.ComponentUtility.PasteComponentAsNew(g);
                        }
                    }

                    if (g.GetComponent<MeleeController>() == null) g.AddComponent<MeleeController>();
                    if (g.GetComponent<HealthController>() == null) g.AddComponent<HealthController>();
                    if (g.GetComponent<SpawnController>() == null) g.AddComponent<SpawnController>();
                    if (g.GetComponent<ShootController>() == null) g.AddComponent<ShootController>();

                    var agent = g.GetComponent<NavMeshAgent>();
                    if (agent != null)
                    {
                        var surfaceObj = GameObject.Find("EnemySurface");
                        if (surfaceObj != null)
                        {
                            var surfComp = surfaceObj.GetComponent(System.Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation") ?? System.Type.GetType("NavMeshSurface"));
                            if (surfComp != null)
                            {
                                var prop = surfComp.GetType().GetProperty("agentTypeID");
                                if (prop != null) agent.agentTypeID = (int)prop.GetValue(surfComp);
                            }
                        }
                        agent.Warp(h.position);
                    }

                    Undo.RegisterCreatedObjectUndo(g, "Fill With Enemies");
                }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(grp.scene);
        }

        [MenuItem("Tools/Enemy/Create Enemy Surface")]
        static void CreateEnemySurface()
        {
            var existing = GameObject.Find("EnemySurface");
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                return;
            }

            GameObject go = new GameObject("EnemySurface");
            System.Type surfaceType = System.Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation") ?? System.Type.GetType("NavMeshSurface");
            if (surfaceType != null) go.AddComponent(surfaceType);

            Undo.RegisterCreatedObjectUndo(go, "Create Enemy Surface");
            Selection.activeGameObject = go;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
