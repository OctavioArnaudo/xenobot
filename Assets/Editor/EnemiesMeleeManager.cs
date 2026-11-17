using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using System.Linq;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Melee;
using Runtime.Scripts.Health;

namespace Editor.Scripts.Enemies
{
    public static class EnemiesMeleeManager
    {
        [MenuItem("Tools/Enemies/EnemiesMeleePrefab")]
        static void GenerateMeleeEnemies()
        {
            var containerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemiesPrefab.prefab");
            var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemigoMelee.prefab") ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Charger.prefab");

            if (itemPrefab == null) return;

            GameObject container = GameObject.Find("EnemiesMeleePrefab");
            if (container == null)
            {
                if (containerPrefab != null)
                {
                    container = (GameObject)PrefabUtility.InstantiatePrefab(containerPrefab);
                    container.name = "EnemiesMeleePrefab";
                }
                else
                {
                    container = new GameObject("EnemiesMeleePrefab");
                }
            }

            for (int i = container.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(container.transform.GetChild(i).gameObject);
            }

            var ts = Terrain.activeTerrains;
            if (ts.Length == 0) return;
            var rnd = new System.Random();
            int count = 8;

            for (int i = 0; i < count; i++)
            {
                var t = ts[rnd.Next(ts.Length)];
                Vector3 tp = t.transform.position, sz = t.terrainData.size;
                Vector3 p = new Vector3(tp.x + (float)rnd.NextDouble() * sz.x, 0f, tp.z + (float)rnd.NextDouble() * sz.z);
                p.y = tp.y + t.SampleHeight(p);

                if (NavMesh.SamplePosition(p, out var h, 100f, NavMesh.AllAreas))
                {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(itemPrefab, container.transform);
                    instance.name = "EnemyMeleePrefab";
                    instance.transform.position = h.position;

                    if (instance.GetComponent<MeleeController>() == null) instance.AddComponent<MeleeController>();
                    if (instance.GetComponent<HealthController>() == null) instance.AddComponent<HealthController>();

                    var agent = instance.GetComponent<NavMeshAgent>();
                    if (agent != null)
                    {
                        agent.agentTypeID = GetOrCreateAgentType("EnemyMelee");
                        agent.Warp(h.position);
                    }

                    Undo.RegisterCreatedObjectUndo(instance, "Generate Melee Enemies");
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(container.scene);
        }

        [MenuItem("Tools/Enemies/SurfaceEnemyMelee")]
        static void CreateMeleeSurface() => CreateSurface("SurfaceEnemyMelee", "EnemyMelee");

        static void CreateSurface(string surfaceName, string typeName)
        {
            var existing = GameObject.Find(surfaceName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var surfacePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Surface.prefab");
            GameObject go;
            if (surfacePrefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(surfacePrefab);
            }
            else
            {
                go = new GameObject(surfaceName);
                System.Type surfaceType = System.Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation") ?? System.Type.GetType("NavMeshSurface");
                if (surfaceType != null) go.AddComponent(surfaceType);
            }

            go.name = surfaceName;

            var surfComp = go.GetComponent(System.Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation") ?? System.Type.GetType("NavMeshSurface"));
            if (surfComp != null)
            {
                var prop = surfComp.GetType().GetProperty("agentTypeID");
                if (prop != null) prop.SetValue(surfComp, GetOrCreateAgentType(typeName));
            }

            Undo.RegisterCreatedObjectUndo(go, $"Create {surfaceName}");
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
