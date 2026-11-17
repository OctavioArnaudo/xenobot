using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Ammos;

namespace Editor.Scripts.Ammos
{
    public static class AmmosManager
    {
        [MenuItem("Tools/Ammos/AmmosPrefab")]
        static void GenerateAmmos()
        {
            BalanceManager balanceMgr = Object.FindFirstObjectByType<BalanceManager>();
            if (balanceMgr == null)
            {
                var balanceGo = GameObject.Find("BalancePrefab");
                if (balanceGo != null) balanceMgr = balanceGo.GetComponent<BalanceManager>();
            }

            var containerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AmmosPrefab.prefab");
            var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AmmoPrefab.prefab");

            if (itemPrefab == null) return;

            GameObject container = GameObject.Find("AmmosPrefab");
            if (container == null)
            {
                if (containerPrefab != null)
                {
                    container = (GameObject)PrefabUtility.InstantiatePrefab(containerPrefab);
                    container.name = "AmmosPrefab";
                }
                else
                {
                    container = new GameObject("AmmosPrefab");
                }
            }

            for (int i = container.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(container.transform.GetChild(i).gameObject);
            }

            var ts = Terrain.activeTerrains;
            if (ts.Length == 0) return;
            var rnd = new System.Random();
            int count = 10;

            for (int i = 0; i < count; i++)
            {
                var t = ts[rnd.Next(ts.Length)];
                Vector3 tp = t.transform.position, sz = t.terrainData.size;
                Vector3 p = new Vector3(tp.x + (float)rnd.NextDouble() * sz.x, 0f, tp.z + (float)rnd.NextDouble() * sz.z);
                p.y = tp.y + t.SampleHeight(p);

                if (NavMesh.SamplePosition(p, out var h, 100f, NavMesh.AllAreas))
                {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(itemPrefab, container.transform);
                    instance.name = "AmmoPrefab";
                    instance.transform.position = h.position;

                    if (instance.GetComponent<AmmosController>() == null)
                    {
                        instance.AddComponent<AmmosController>();
                    }

                    Undo.RegisterCreatedObjectUndo(instance, "Generate Ammos");
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(container.scene);
        }
    }
}
