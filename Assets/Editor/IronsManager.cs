using UnityEngine;
using UnityEditor;
using UnityEngine.AI;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Irons;

namespace Editor.Scripts.Irons
{
    public static class IronsManager
    {
        [MenuItem("Tools/Irons/IronsPrefab")]
        static void GenerateIrons()
        {
            BalanceManager balanceMgr = Object.FindFirstObjectByType<BalanceManager>();
            if (balanceMgr == null)
            {
                var balanceGo = GameObject.Find("BalancePrefab");
                if (balanceGo != null) balanceMgr = balanceGo.GetComponent<BalanceManager>();
            }

            var ironsPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/IronsPrefab.prefab");
            var ironPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/IronPrefab.prefab");

            if (ironPrefabAsset == null) return;

            GameObject container = GameObject.Find("IronsPrefab");
            if (container == null)
            {
                if (ironsPrefabAsset != null)
                {
                    container = (GameObject)PrefabUtility.InstantiatePrefab(ironsPrefabAsset);
                    container.name = "IronsPrefab";
                }
                else
                {
                    container = new GameObject("IronsPrefab");
                }
            }

            for (int i = container.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(container.transform.GetChild(i).gameObject);
            }

            var ts = Terrain.activeTerrains;
            if (ts.Length == 0) return;
            var rnd = new System.Random();
            int count = 15;

            for (int i = 0; i < count; i++)
            {
                var t = ts[rnd.Next(ts.Length)];
                Vector3 tp = t.transform.position, sz = t.terrainData.size;
                Vector3 p = new Vector3(tp.x + (float)rnd.NextDouble() * sz.x, 0f, tp.z + (float)rnd.NextDouble() * sz.z);
                p.y = tp.y + t.SampleHeight(p);

                if (NavMesh.SamplePosition(p, out var h, 100f, NavMesh.AllAreas))
                {
                    GameObject ironInstance = (GameObject)PrefabUtility.InstantiatePrefab(ironPrefabAsset, container.transform);
                    ironInstance.name = "IronPrefab";
                    ironInstance.transform.position = h.position;

                    if (ironInstance.GetComponent<IronsController>() == null)
                    {
                        ironInstance.AddComponent<IronsController>();
                    }

                    Undo.RegisterCreatedObjectUndo(ironInstance, "Generate Irons");
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(container.scene);
        }
    }
}
