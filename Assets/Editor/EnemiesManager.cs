using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using System.Linq;
using System.Collections.Generic;
using Runtime.Scripts.Melee;
using Runtime.Scripts.Health;
using Runtime.Scripts.Spawn;
using Runtime.Scripts.Shoot;
using Runtime.Scripts.Balance;

namespace Editor.Scripts.Enemies
{
    public static class EnemiesManager
    {
        struct EnemyEntry
        {
            public GameObject prefab;
            public string agentTypeName;
            public HostType hostType;
        }

        [MenuItem("Tools/Enemies/EnemiesPrefab")]
        static void FillWithEnemies()
        {
            var ts = Terrain.activeTerrains;
            if (ts.Length == 0) return;

            var containerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemiesPrefab.prefab");

            GameObject existingGrp = GameObject.Find("EnemiesPrefab");
            if (existingGrp != null)
            {
                Object.DestroyImmediate(existingGrp);
            }

            GameObject grp;
            if (containerPrefab != null)
            {
                grp = (GameObject)PrefabUtility.InstantiatePrefab(containerPrefab);
                grp.name = "EnemiesPrefab";
            }
            else
            {
                grp = new GameObject("EnemiesPrefab");
            }

            var rnd = new System.Random();
            var pool = new List<EnemyEntry>();

            foreach (var p in LoadVariants("EnemyMeleePrefab"))
                pool.Add(new EnemyEntry { prefab = p, agentTypeName = "EnemyMelee", hostType = HostType.Melee });
            foreach (var p in LoadVariants("EnemyRangePrefab"))
                pool.Add(new EnemyEntry { prefab = p, agentTypeName = "EnemyRange", hostType = HostType.Range });
            foreach (var p in LoadVariants("EnemyHybridPrefab"))
                pool.Add(new EnemyEntry { prefab = p, agentTypeName = "EnemyHybrid", hostType = HostType.Hybrid });

            if (pool.Count == 0)
            {
                var fallback = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemyPrefab (1).prefab");
                if (fallback != null) pool.Add(new EnemyEntry { prefab = fallback, agentTypeName = "Enemy", hostType = HostType.Melee });
            }

            if (pool.Count == 0) return;

            int count = 20;
            for (int i = 0; i < count; i++)
            {
                var entry = pool[rnd.Next(pool.Count)];
                var t = ts[rnd.Next(ts.Length)];
                Vector3 tp = t.transform.position, sz = t.terrainData.size;
                Vector3 p = new Vector3(tp.x + (float)rnd.NextDouble() * sz.x, 0f, tp.z + (float)rnd.NextDouble() * sz.z);
                p.y = tp.y + t.SampleHeight(p);

                if (NavMesh.SamplePosition(p, out var h, 100f, NavMesh.AllAreas))
                {
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, grp.transform);
                    g.name = entry.prefab.name;
                    g.transform.position = h.position;

                    if ((entry.hostType == HostType.Melee || entry.hostType == HostType.Hybrid) && g.GetComponent<MeleeController>() == null)
                        g.AddComponent<MeleeController>();
                    if ((entry.hostType == HostType.Range || entry.hostType == HostType.Hybrid) && g.GetComponent<ShootController>() == null)
                        g.AddComponent<ShootController>();
                    if (g.GetComponent<HealthController>() == null)
                        g.AddComponent<HealthController>();

                    var agent = g.GetComponent<NavMeshAgent>();
                    if (agent != null)
                    {
                        agent.agentTypeID = GetOrCreateAgentType(entry.agentTypeName);
                        agent.Warp(h.position);
                    }

                    Undo.RegisterCreatedObjectUndo(g, "Fill With Enemies");
                }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(grp.scene);
        }

        static List<GameObject> LoadVariants(string prefix)
        {
            var list = new List<GameObject>();
            for (int i = 1; i <= 20; i++)
            {
                string path = $"Assets/Prefabs/{prefix} ({i}).prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) list.Add(prefab);
                else if (i > 5) break;
            }
            return list;
        }

        [MenuItem("Tools/Enemies/SurfaceEnemy")]
        static void CreateEnemySurface()
        {
            var existing = GameObject.Find("SurfaceEnemy");
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                return;
            }

            var surfacePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Surface.prefab");
            GameObject go;
            if (surfacePrefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(surfacePrefab);
            }
            else
            {
                go = new GameObject("SurfaceEnemy");
                System.Type surfaceType = System.Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation") ?? System.Type.GetType("NavMeshSurface");
                if (surfaceType != null) go.AddComponent(surfaceType);
            }

            go.name = "SurfaceEnemy";

            var surfComp = go.GetComponent(System.Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation") ?? System.Type.GetType("NavMeshSurface"));
            if (surfComp != null)
            {
                var prop = surfComp.GetType().GetProperty("agentTypeID");
                if (prop != null) prop.SetValue(surfComp, GetOrCreateAgentType("Enemy"));
            }

            Undo.RegisterCreatedObjectUndo(go, "Create Enemy Surface");
            Selection.activeGameObject = go;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        }

        static int GetOrCreateAgentType(string typeName)
        {
            var a = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset").FirstOrDefault();
            var so = a ? new SerializedObject(a) : null;
            var names = so?.FindProperty("m_SettingNames");
            var set = so?.FindProperty("m_Settings");
            if (names == null || set == null) return 0;

            int i = -1;
            for (int k = 0; k < names.arraySize; k++)
            {
                if (names.GetArrayElementAtIndex(k).stringValue == typeName)
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
                names.GetArrayElementAtIndex(i).stringValue = typeName;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }

            var e = set.GetArrayElementAtIndex(i);
            return e.FindPropertyRelative("agentTypeID")?.intValue ?? 0;
        }
    }
}
