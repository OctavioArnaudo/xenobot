using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using System.Linq;
using System.Collections.Generic;
using Runtime.Scripts.Melee;
using Runtime.Scripts.Health;
using Runtime.Scripts.Spawn;
using Runtime.Scripts.Shoot;

namespace Editor.Scripts.Enemies
{
    public static class EnemiesManager
    {
        [MenuItem("Tools/Enemies/EnemiesPrefab")]
        static void FillWithEnemies()
        {
            var ts = Terrain.activeTerrains;
            if (ts.Length == 0) return;

            var grp = GameObject.Find("EnemiesPrefab") ?? new GameObject("EnemiesPrefab");
            var rnd = new System.Random();

            var variants = LoadVariants("EnemyPrefab");
            if (variants.Count == 0) return;

            for (int i = 0; i < 20; i++)
            {
                var itemPrefab = variants[rnd.Next(variants.Count)];
                var t = ts[rnd.Next(ts.Length)];
                Vector3 tp = t.transform.position, sz = t.terrainData.size;
                Vector3 p = new Vector3(tp.x + (float)rnd.NextDouble() * sz.x, 0f, tp.z + (float)rnd.NextDouble() * sz.z);
                p.y = tp.y + t.SampleHeight(p);

                if (NavMesh.SamplePosition(p, out var h, 100f, NavMesh.AllAreas))
                {
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(itemPrefab, grp.transform);
                    g.name = "EnemyPrefab";
                    g.transform.position = h.position;

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
            if (list.Count == 0)
            {
                var single = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{prefix}.prefab");
                if (single != null) list.Add(single);
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
