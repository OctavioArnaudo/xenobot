using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using System.Linq;
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
                        agent.agentTypeID = GetOrCreateEnemyAgentType();
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
            if (surfaceType != null)
            {
                var surfComp = go.AddComponent(surfaceType);
                var prop = surfComp.GetType().GetProperty("agentTypeID");
                if (prop != null) prop.SetValue(surfComp, GetOrCreateEnemyAgentType());
            }

            Undo.RegisterCreatedObjectUndo(go, "Create Enemy Surface");
            Selection.activeGameObject = go;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        }

        static int GetOrCreateEnemyAgentType()
        {
            var a = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset").FirstOrDefault();
            var so = a ? new SerializedObject(a) : null;
            var names = so?.FindProperty("m_SettingNames");
            var set = so?.FindProperty("m_Settings");
            if (names == null || set == null) return 0;

            int i = -1;
            for (int k = 0; k < names.arraySize; k++)
            {
                if (names.GetArrayElementAtIndex(k).stringValue == "Enemy")
                {
                    i = k;
                    break;
                }
            }

            if (i < 0)
            {
                int id = NavMesh.CreateSettings().agentTypeID;
                so.Update();
                for (int k = 0; k < set.arraySize; k++)
                {
                    if (set.GetArrayElementAtIndex(k).FindPropertyRelative("agentTypeID")?.intValue == id)
                    {
                        i = k;
                        break;
                    }
                }
                if (i < 0) return 0;
                while (names.arraySize <= i) names.InsertArrayElementAtIndex(names.arraySize);
                names.GetArrayElementAtIndex(i).stringValue = "Enemy";
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }

            var e = set.GetArrayElementAtIndex(i);
            return e.FindPropertyRelative("agentTypeID")?.intValue ?? 0;
        }
    }
}
