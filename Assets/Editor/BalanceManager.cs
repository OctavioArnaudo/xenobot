using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Pickup;

namespace Editor.Scripts.Balance
{
    public static class BalanceManagerEditor
    {
        [MenuItem("Tools/Balance/BalancePrefab")]
        static void BalancePrefab()
        {
            var existing = GameObject.Find("BalancePrefab");
            if (existing != null) Object.DestroyImmediate(existing);

            GameObject go = new GameObject("BalancePrefab");
            var bm = go.AddComponent<BalanceManager>();

            RegisterEnemies();
            RegisterLifes();
            RegisterShields();
            RegisterIrons();
            RegisterAmmos();
            RegisterFuels();

            var propPoints = new List<PropSpawnPointData>();
            var smashables = Object.FindObjectsByType<Smashable>(FindObjectsSortMode.None);
            foreach (var sm in smashables)
            {
                if (sm == null) continue;
                propPoints.Add(new PropSpawnPointData { pointTransform = sm.transform, categoryToSpawn = PropType.Mineral, customPrefab = null });
            }

            SerializedObject so = new SerializedObject(bm);
            var prProp = so.FindProperty("propSpawnPoints");
            prProp.arraySize = 0;
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

        [MenuItem("Tools/Balance/LifesPrefab")]
        static void RegisterLifes() => RegisterContainerItems("LifesPrefab", ItemType.Consumible);

        [MenuItem("Tools/Balance/ShieldsPrefab")]
        static void RegisterShields() => RegisterContainerItems("ShieldsPrefab", ItemType.Thing);

        [MenuItem("Tools/Balance/IronsPrefab")]
        static void RegisterIrons() => RegisterContainerItems("IronsPrefab", ItemType.Resource);

        [MenuItem("Tools/Balance/AmmosPrefab")]
        static void RegisterAmmos() => RegisterContainerItems("AmmosPrefab", ItemType.Consumible);

        [MenuItem("Tools/Balance/FuelsPrefab")]
        static void RegisterFuels() => RegisterContainerItems("FuelsPrefab", ItemType.Consumible);

        [MenuItem("Tools/Balance/EnemiesPrefab")]
        static void RegisterEnemies() => RegisterContainerHosts("EnemiesPrefab");

        static BalanceManager GetOrCreateBalanceManager()
        {
            BalanceManager bm = Object.FindFirstObjectByType<BalanceManager>();
            if (bm == null)
            {
                var balanceGo = GameObject.Find("BalancePrefab") ?? new GameObject("BalancePrefab");
                bm = balanceGo.GetComponent<BalanceManager>() ?? balanceGo.AddComponent<BalanceManager>();
            }
            return bm;
        }

        static void RegisterContainerItems(string containerName, ItemType itemType)
        {
            var bm = GetOrCreateBalanceManager();
            var container = GameObject.Find(containerName);
            if (container == null) return;

            SerializedObject so = new SerializedObject(bm);
            var itProp = so.FindProperty("itemSpawnPoints");

            foreach (Transform child in container.transform)
            {
                if (child == null) continue;
                int idx = itProp.arraySize;
                itProp.InsertArrayElementAtIndex(idx);
                var elem = itProp.GetArrayElementAtIndex(idx);
                elem.FindPropertyRelative("pointTransform").objectReferenceValue = child;
                elem.FindPropertyRelative("categoryToSpawn").enumValueIndex = (int)itemType;
            }

            so.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bm.gameObject.scene);
        }

        static void RegisterContainerHosts(string containerName)
        {
            var bm = GetOrCreateBalanceManager();
            var container = GameObject.Find(containerName);
            if (container == null) return;

            SerializedObject so = new SerializedObject(bm);
            var spProp = so.FindProperty("hostSpawnPoints");

            foreach (Transform child in container.transform)
            {
                if (child == null) continue;
                int idx = spProp.arraySize;
                spProp.InsertArrayElementAtIndex(idx);
                var elem = spProp.GetArrayElementAtIndex(idx);
                elem.FindPropertyRelative("pointTransform").objectReferenceValue = child;
                elem.FindPropertyRelative("enemyTypeToSpawn").enumValueIndex = (int)HostType.Melee;
            }

            so.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bm.gameObject.scene);
        }

        static void PopulateBalanceManager(BalanceManager bm)
        {
            var hostPoints = new List<HostSpawnPointData>();
            var itemPoints = new List<ItemSpawnPointData>();
            var propPoints = new List<PropSpawnPointData>();

            string[] hostContainers = { "EnemiesPrefab" };
            foreach (var hc in hostContainers)
            {
                var c = GameObject.Find(hc);
                if (c != null)
                {
                    foreach (Transform child in c.transform)
                    {
                        hostPoints.Add(new HostSpawnPointData { pointTransform = child, enemyTypeToSpawn = HostType.Melee, customPrefab = null });
                    }
                }
            }

            string[] itemContainers = { "LifesPrefab", "ShieldsPrefab", "IronsPrefab", "AmmosPrefab", "FuelsPrefab" };
            foreach (var ic in itemContainers)
            {
                var c = GameObject.Find(ic);
                if (c != null)
                {
                    ItemType iType = ic.Contains("Iron") ? ItemType.Resource : ItemType.Consumible;
                    foreach (Transform child in c.transform)
                    {
                        itemPoints.Add(new ItemSpawnPointData { pointTransform = child, categoryToSpawn = iType, customPrefab = null });
                    }
                }
            }

            var enemies = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in enemies)
            {
                if (obj == null) continue;
                bool isEnemy = obj.CompareTag("Enemy") || obj.GetComponent<Runtime.Scripts.Enemy.EnemyController>() != null || obj.GetComponent<ChargerAI>() != null;
                if (isEnemy && obj.transform.parent == null)
                {
                    hostPoints.Add(new HostSpawnPointData { pointTransform = obj.transform, enemyTypeToSpawn = HostType.Melee, customPrefab = null });
                }
            }

            var pickups = Object.FindObjectsByType<PickupController>(FindObjectsSortMode.None);
            foreach (var pk in pickups)
            {
                if (pk == null) continue;
                itemPoints.Add(new ItemSpawnPointData { pointTransform = pk.transform, categoryToSpawn = ItemType.Thing, customPrefab = null });
            }

            var smashables = Object.FindObjectsByType<Smashable>(FindObjectsSortMode.None);
            foreach (var sm in smashables)
            {
                if (sm == null) continue;
                propPoints.Add(new PropSpawnPointData { pointTransform = sm.transform, categoryToSpawn = PropType.Mineral, customPrefab = null });
            }

            SerializedObject so = new SerializedObject(bm);

            var spProp = so.FindProperty("hostSpawnPoints");
            spProp.arraySize = 0;
            for (int i = 0; i < hostPoints.Count; i++)
            {
                spProp.InsertArrayElementAtIndex(i);
                var elem = spProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("pointTransform").objectReferenceValue = hostPoints[i].pointTransform;
                elem.FindPropertyRelative("enemyTypeToSpawn").enumValueIndex = (int)hostPoints[i].enemyTypeToSpawn;
            }

            var itProp = so.FindProperty("itemSpawnPoints");
            itProp.arraySize = 0;
            for (int i = 0; i < itemPoints.Count; i++)
            {
                itProp.InsertArrayElementAtIndex(i);
                var elem = itProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("pointTransform").objectReferenceValue = itemPoints[i].pointTransform;
                elem.FindPropertyRelative("categoryToSpawn").enumValueIndex = (int)itemPoints[i].categoryToSpawn;
            }

            var prProp = so.FindProperty("propSpawnPoints");
            prProp.arraySize = 0;
            for (int i = 0; i < propPoints.Count; i++)
            {
                prProp.InsertArrayElementAtIndex(i);
                var elem = prProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("pointTransform").objectReferenceValue = propPoints[i].pointTransform;
                elem.FindPropertyRelative("categoryToSpawn").enumValueIndex = (int)propPoints[i].categoryToSpawn;
            }

            so.ApplyModifiedProperties();
        }
    }
}
