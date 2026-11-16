using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Pickup;

namespace Editor.Scripts.Balance
{
    public static class BalanceManagerEditor
    {
        [MenuItem("Tools/Balance/Create BalancePrefab")]
        static void CreateBalancePrefab()
        {
            var existing = GameObject.Find("BalancePrefab");
            if (existing != null) Object.DestroyImmediate(existing);

            GameObject go = new GameObject("BalancePrefab");
            var bm = go.AddComponent<BalanceManager>();

            var hostPoints = new List<HostSpawnPointData>();
            var itemPoints = new List<ItemSpawnPointData>();
            var propPoints = new List<PropSpawnPointData>();

            var enemies = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in enemies)
            {
                if (obj == null) continue;
                bool isEnemy = obj.CompareTag("Enemy") || obj.GetComponent<Runtime.Scripts.Enemy.EnemyController>() != null || obj.GetComponent<ChargerAI>() != null;
                if (isEnemy && obj.transform.parent == null)
                {
                    HostType hType = HostType.Melee;
                    if (obj.name.ToLower().Contains("range")) hType = HostType.Range;
                    else if (obj.name.ToLower().Contains("hybrid")) hType = HostType.Hybrid;

                    hostPoints.Add(new HostSpawnPointData
                    {
                        pointTransform = obj.transform,
                        enemyTypeToSpawn = hType,
                        customPrefab = null
                    });
                }
            }

            var pickups = Object.FindObjectsByType<PickupController>(FindObjectsSortMode.None);
            foreach (var pk in pickups)
            {
                if (pk == null) continue;
                itemPoints.Add(new ItemSpawnPointData
                {
                    pointTransform = pk.transform,
                    categoryToSpawn = ItemType.Thing,
                    customPrefab = null
                });
            }

            var smashables = Object.FindObjectsByType<Smashable>(FindObjectsSortMode.None);
            foreach (var sm in smashables)
            {
                if (sm == null) continue;
                PropType pType = PropType.Mineral;
                string n = sm.gameObject.name.ToLower();
                if (n.Contains("tree")) pType = PropType.Tree;
                else if (n.Contains("building")) pType = PropType.Building;
                else if (n.Contains("plant")) pType = PropType.Plant;

                propPoints.Add(new PropSpawnPointData
                {
                    pointTransform = sm.transform,
                    categoryToSpawn = pType,
                    customPrefab = null
                });
            }

            SerializedObject so = new SerializedObject(bm);

            var spProp = so.FindProperty("hostSpawnPoints");
            spProp.clearArray();
            for (int i = 0; i < hostPoints.Count; i++)
            {
                spProp.InsertArrayElementAtIndex(i);
                var elem = spProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("pointTransform").objectReferenceValue = hostPoints[i].pointTransform;
                elem.FindPropertyRelative("enemyTypeToSpawn").enumValueIndex = (int)hostPoints[i].enemyTypeToSpawn;
            }

            var itProp = so.FindProperty("itemSpawnPoints");
            itProp.clearArray();
            for (int i = 0; i < itemPoints.Count; i++)
            {
                itProp.InsertArrayElementAtIndex(i);
                var elem = itProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("pointTransform").objectReferenceValue = itemPoints[i].pointTransform;
                elem.FindPropertyRelative("categoryToSpawn").enumValueIndex = (int)itemPoints[i].categoryToSpawn;
            }

            var prProp = so.FindProperty("propSpawnPoints");
            prProp.clearArray();
            for (int i = 0; i < propPoints.Count; i++)
            {
                prProp.InsertArrayElementAtIndex(i);
                var elem = prProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("pointTransform").objectReferenceValue = propPoints[i].pointTransform;
                elem.FindPropertyRelative("categoryToSpawn").enumValueIndex = (int)propPoints[i].categoryToSpawn;
            }

            so.ApplyModifiedProperties();

            Undo.RegisterCreatedObjectUndo(go, "Create BalancePrefab");
            Selection.activeGameObject = go;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
