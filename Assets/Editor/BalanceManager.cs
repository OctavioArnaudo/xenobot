using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Pickup;
using Runtime.Scripts.Melee;
using Runtime.Scripts.Shoot;
using Runtime.Scripts.Health;
using Runtime.Scripts.Irons;
using Runtime.Scripts.Lifes;
using Runtime.Scripts.Shields;
using Runtime.Scripts.Ammos;
using Runtime.Scripts.Fuels;

namespace Editor.Scripts.Balance
{
    public static class BalanceManagerEditor
    {
        [MenuItem("Tools/Balance/BalancePrefab")]
        static void BalancePrefab()
        {
            var existing = GameObject.Find("BalancePrefab");
            if (existing != null) Object.DestroyImmediate(existing);

            var balancePrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BalancePrefab.prefab");
            GameObject go;
            if (balancePrefabAsset != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(balancePrefabAsset);
            }
            else
            {
                go = new GameObject("BalancePrefab");
            }

            go.name = "BalancePrefab";
            var bm = go.GetComponent<BalanceManager>() ?? go.AddComponent<BalanceManager>();

            PopulateBalanceManager(bm);

            Undo.RegisterCreatedObjectUndo(go, "Create BalancePrefab");
            Selection.activeGameObject = go;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        }

        [MenuItem("Tools/Balance/BalancePrefab.LifesPrefab")]
        static void RegisterLifes() => RegisterByComponent<LifesController>("LifesPrefab", ItemType.Consumible, "Assets/Prefabs/LifePrefab.prefab");

        [MenuItem("Tools/Balance/BalancePrefab.ShieldsPrefab")]
        static void RegisterShields() => RegisterByComponent<ShieldsController>("ShieldsPrefab", ItemType.Thing, "Assets/Prefabs/ShieldPrefab.prefab");

        [MenuItem("Tools/Balance/BalancePrefab.IronsPrefab")]
        static void RegisterIrons() => RegisterByComponent<IronsController>("IronsPrefab", ItemType.Resource, "Assets/Prefabs/IronPrefab.prefab");

        [MenuItem("Tools/Balance/BalancePrefab.AmmosPrefab")]
        static void RegisterAmmos() => RegisterByComponent<AmmosController>("AmmosPrefab", ItemType.Consumible, "Assets/Prefabs/AmmoPrefab.prefab");

        [MenuItem("Tools/Balance/BalancePrefab.FuelsPrefab")]
        static void RegisterFuels() => RegisterByComponent<FuelsController>("FuelsPrefab", ItemType.Consumible, "Assets/Prefabs/FuelPrefab.prefab");

        [MenuItem("Tools/Balance/BalancePrefab.EnemiesPrefab")]
        static void RegisterEnemies() => RegisterHostsGeneral();

        [MenuItem("Tools/Balance/BalancePrefab.EnemiesMeleePrefab")]
        static void RegisterEnemiesMelee() => RegisterContainerHostsWithType("EnemiesMeleePrefab", HostType.Melee, "Assets/Prefabs/EnemyMeleePrefab (1).prefab");

        [MenuItem("Tools/Balance/BalancePrefab.EnemiesRangePrefab")]
        static void RegisterEnemiesRange() => RegisterContainerHostsWithType("EnemiesRangePrefab", HostType.Range, "Assets/Prefabs/EnemyRangePrefab (1).prefab");

        [MenuItem("Tools/Balance/BalancePrefab.EnemiesHybridPrefab")]
        static void RegisterEnemiesHybrid() => RegisterContainerHostsWithType("EnemiesHybridPrefab", HostType.Hybrid, "Assets/Prefabs/EnemyHybridPrefab (1).prefab");

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

        static GameObject GetSourcePrefab(GameObject go)
        {
            if (go == null) return null;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            return source != null ? source as GameObject : null;
        }

        static void RegisterByComponent<T>(string containerName, ItemType itemType, string defaultPrefabPath) where T : MonoBehaviour
        {
            var bm = GetOrCreateBalanceManager();
            SerializedObject so = new SerializedObject(bm);
            var itProp = so.FindProperty("itemSpawnPoints");

            var items = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            var defaultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(defaultPrefabPath);

            foreach (var item in items)
            {
                if (item == null) continue;
                int idx = itProp.arraySize;
                itProp.InsertArrayElementAtIndex(idx);
                var elem = itProp.GetArrayElementAtIndex(idx);
                elem.FindPropertyRelative("pointTransform").objectReferenceValue = item.transform;
                elem.FindPropertyRelative("categoryToSpawn").enumValueIndex = (int)itemType;

                GameObject source = GetSourcePrefab(item.gameObject) ?? defaultPrefab;
                elem.FindPropertyRelative("customPrefab").objectReferenceValue = source;
            }

            PopulateMasterPrefabReferences(so);
            so.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bm.gameObject.scene);
        }

        static void RegisterHostsGeneral()
        {
            var bm = GetOrCreateBalanceManager();
            SerializedObject so = new SerializedObject(bm);
            var spProp = so.FindProperty("hostSpawnPoints");

            var enemies = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in enemies)
            {
                if (obj == null) continue;
                bool isEnemy = obj.CompareTag("Enemy") || obj.GetComponent<Runtime.Scripts.Enemy.EnemyController>() != null || obj.GetComponent<ChargerAI>() != null;
                if (isEnemy)
                {
                    HostType hType = HostType.Melee;
                    if (obj.name.ToLower().Contains("range") || obj.GetComponent<ShootController>() != null) hType = HostType.Range;
                    else if (obj.name.ToLower().Contains("hybrid") || (obj.GetComponent<ShootController>() != null && obj.GetComponent<MeleeController>() != null)) hType = HostType.Hybrid;

                    int idx = spProp.arraySize;
                    spProp.InsertArrayElementAtIndex(idx);
                    var elem = spProp.GetArrayElementAtIndex(idx);
                    elem.FindPropertyRelative("pointTransform").objectReferenceValue = obj.transform;
                    elem.FindPropertyRelative("enemyTypeToSpawn").enumValueIndex = (int)hType;

                    string defaultPath = hType == HostType.Range ? "Assets/Prefabs/EnemyRangePrefab (1).prefab" : hType == HostType.Hybrid ? "Assets/Prefabs/EnemyHybridPrefab (1).prefab" : "Assets/Prefabs/EnemyMeleePrefab (1).prefab";
                    elem.FindPropertyRelative("customPrefab").objectReferenceValue = GetSourcePrefab(obj) ?? AssetDatabase.LoadAssetAtPath<GameObject>(defaultPath);
                }
            }

            PopulateMasterPrefabReferences(so);
            so.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bm.gameObject.scene);
        }

        static void RegisterContainerHostsWithType(string containerName, HostType hostType, string defaultPrefabPath)
        {
            var bm = GetOrCreateBalanceManager();
            SerializedObject so = new SerializedObject(bm);
            var spProp = so.FindProperty("hostSpawnPoints");
            var defaultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(defaultPrefabPath);

            var container = GameObject.Find(containerName);
            if (container != null)
            {
                foreach (Transform child in container.transform)
                {
                    if (child == null) continue;
                    int idx = spProp.arraySize;
                    spProp.InsertArrayElementAtIndex(idx);
                    var elem = spProp.GetArrayElementAtIndex(idx);
                    elem.FindPropertyRelative("pointTransform").objectReferenceValue = child;
                    elem.FindPropertyRelative("enemyTypeToSpawn").enumValueIndex = (int)hostType;
                    elem.FindPropertyRelative("customPrefab").objectReferenceValue = GetSourcePrefab(child.gameObject) ?? defaultPrefab;
                }
            }

            PopulateMasterPrefabReferences(so);
            so.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bm.gameObject.scene);
        }

        static void PopulateBalanceManager(BalanceManager bm)
        {
            var hostPoints = new List<HostSpawnPointData>();
            var itemPoints = new List<ItemSpawnPointData>();
            var propPoints = new List<PropSpawnPointData>();

            string[] hostContainers = { "EnemiesPrefab", "EnemiesMeleePrefab", "EnemiesRangePrefab", "EnemiesHybridPrefab", "Enemies", "Chargers" };
            foreach (var hc in hostContainers)
            {
                var c = GameObject.Find(hc);
                if (c != null)
                {
                    HostType hType = HostType.Melee;
                    if (hc.Contains("Range")) hType = HostType.Range;
                    else if (hc.Contains("Hybrid")) hType = HostType.Hybrid;
                    else if (hc.Contains("Melee")) hType = HostType.Melee;

                    string defPath = "Assets/Prefabs/EnemyMeleePrefab (1).prefab";
                    if (hType == HostType.Range) defPath = "Assets/Prefabs/EnemyRangePrefab (1).prefab";
                    else if (hType == HostType.Hybrid) defPath = "Assets/Prefabs/EnemyHybridPrefab (1).prefab";
                    GameObject defPref = AssetDatabase.LoadAssetAtPath<GameObject>(defPath);

                    foreach (Transform child in c.transform)
                    {
                        if (child == null) continue;
                        GameObject prefabSrc = GetSourcePrefab(child.gameObject) ?? defPref;
                        if (!hostPoints.Exists(x => x.pointTransform == child))
                        {
                            hostPoints.Add(new HostSpawnPointData { pointTransform = child, enemyTypeToSpawn = hType, customPrefab = prefabSrc });
                        }
                    }
                }
            }

            var enemies = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in enemies)
            {
                if (obj == null) continue;
                bool isEnemy = obj.CompareTag("Enemy") || obj.GetComponent<Runtime.Scripts.Enemy.EnemyController>() != null || obj.GetComponent<ChargerAI>() != null;
                if (isEnemy)
                {
                    HostType hType = HostType.Melee;
                    if (obj.name.ToLower().Contains("range") || obj.GetComponent<ShootController>() != null) hType = HostType.Range;
                    else if (obj.name.ToLower().Contains("hybrid") || (obj.GetComponent<ShootController>() != null && obj.GetComponent<MeleeController>() != null)) hType = HostType.Hybrid;

                    GameObject prefabSrc = GetSourcePrefab(obj);
                    if (prefabSrc == null)
                    {
                        string path = hType == HostType.Range ? "Assets/Prefabs/EnemyRangePrefab (1).prefab" : hType == HostType.Hybrid ? "Assets/Prefabs/EnemyHybridPrefab (1).prefab" : "Assets/Prefabs/EnemyMeleePrefab (1).prefab";
                        prefabSrc = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    }

                    if (!hostPoints.Exists(x => x.pointTransform == obj.transform))
                    {
                        hostPoints.Add(new HostSpawnPointData { pointTransform = obj.transform, enemyTypeToSpawn = hType, customPrefab = prefabSrc });
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
                    string defPath = "Assets/Prefabs/ExperiencePrefab.prefab";
                    if (ic.Contains("Iron")) defPath = "Assets/Prefabs/IronPrefab.prefab";
                    else if (ic.Contains("Life")) defPath = "Assets/Prefabs/LifePrefab.prefab";
                    else if (ic.Contains("Shield")) defPath = "Assets/Prefabs/ShieldPrefab.prefab";
                    else if (ic.Contains("Ammo")) defPath = "Assets/Prefabs/AmmoPrefab.prefab";
                    else if (ic.Contains("Fuel")) defPath = "Assets/Prefabs/FuelPrefab.prefab";
                    GameObject defPref = AssetDatabase.LoadAssetAtPath<GameObject>(defPath);

                    foreach (Transform child in c.transform)
                    {
                        if (child == null) continue;
                        GameObject prefabSrc = GetSourcePrefab(child.gameObject) ?? defPref;
                        if (!itemPoints.Exists(x => x.pointTransform == child))
                        {
                            itemPoints.Add(new ItemSpawnPointData { pointTransform = child, categoryToSpawn = iType, customPrefab = prefabSrc });
                        }
                    }
                }
            }

            var pickups = Object.FindObjectsByType<PickupController>(FindObjectsSortMode.None);
            foreach (var pk in pickups)
            {
                if (pk == null) continue;
                ItemType iType = ItemType.Thing;
                string n = pk.gameObject.name.ToLower();
                if (n.Contains("iron")) iType = ItemType.Resource;
                else if (n.Contains("life") || n.Contains("shield") || n.Contains("ammo") || n.Contains("fuel")) iType = ItemType.Consumible;

                GameObject prefabSrc = GetSourcePrefab(pk.gameObject);
                if (prefabSrc == null)
                {
                    string path = "Assets/Prefabs/ExperiencePrefab.prefab";
                    if (n.Contains("iron")) path = "Assets/Prefabs/IronPrefab.prefab";
                    else if (n.Contains("life")) path = "Assets/Prefabs/LifePrefab.prefab";
                    else if (n.Contains("shield")) path = "Assets/Prefabs/ShieldPrefab.prefab";
                    else if (n.Contains("ammo")) path = "Assets/Prefabs/AmmoPrefab.prefab";
                    else if (n.Contains("fuel")) path = "Assets/Prefabs/FuelPrefab.prefab";
                    prefabSrc = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }

                if (!itemPoints.Exists(x => x.pointTransform == pk.transform))
                {
                    itemPoints.Add(new ItemSpawnPointData { pointTransform = pk.transform, categoryToSpawn = iType, customPrefab = prefabSrc });
                }
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

                GameObject prefabSrc = GetSourcePrefab(sm.gameObject);
                if (prefabSrc == null)
                {
                    string path = "Assets/Prefabs/MineralsPrefab.prefab";
                    if (n.Contains("tree")) path = "Assets/Prefabs/TreePrefab.prefab";
                    else if (n.Contains("building")) path = "Assets/Prefabs/BuildingsPrefab.prefab";
                    else if (n.Contains("plant")) path = "Assets/Prefabs/PlantsPrefab.prefab";
                    prefabSrc = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }

                if (!propPoints.Exists(x => x.pointTransform == sm.transform))
                {
                    propPoints.Add(new PropSpawnPointData { pointTransform = sm.transform, categoryToSpawn = pType, customPrefab = prefabSrc });
                }
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
                elem.FindPropertyRelative("customPrefab").objectReferenceValue = hostPoints[i].customPrefab;
            }

            var itProp = so.FindProperty("itemSpawnPoints");
            itProp.arraySize = 0;
            for (int i = 0; i < itemPoints.Count; i++)
            {
                itProp.InsertArrayElementAtIndex(i);
                var elem = itProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("pointTransform").objectReferenceValue = itemPoints[i].pointTransform;
                elem.FindPropertyRelative("categoryToSpawn").enumValueIndex = (int)itemPoints[i].categoryToSpawn;
                elem.FindPropertyRelative("customPrefab").objectReferenceValue = itemPoints[i].customPrefab;
            }

            var prProp = so.FindProperty("propSpawnPoints");
            prProp.arraySize = 0;
            for (int i = 0; i < propPoints.Count; i++)
            {
                prProp.InsertArrayElementAtIndex(i);
                var elem = prProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("pointTransform").objectReferenceValue = propPoints[i].pointTransform;
                elem.FindPropertyRelative("categoryToSpawn").enumValueIndex = (int)propPoints[i].categoryToSpawn;
                elem.FindPropertyRelative("customPrefab").objectReferenceValue = propPoints[i].customPrefab;
            }

            PopulateMasterPrefabReferences(so);
            so.ApplyModifiedProperties();
        }

        static void PopulateMasterPrefabReferences(SerializedObject so)
        {
            var hostMeleeRef = so.FindProperty("hostMeleePrefab");
            if (hostMeleeRef != null) hostMeleeRef.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemyMeleePrefab (1).prefab") ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemigoMelee.prefab");

            var hostRangeRef = so.FindProperty("hostRangePrefab");
            if (hostRangeRef != null) hostRangeRef.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemyRangePrefab (1).prefab") ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TurretPrefab.prefab");

            var hostHybridRef = so.FindProperty("hostHybridPrefab");
            if (hostHybridRef != null) hostHybridRef.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemyHybridPrefab (1).prefab") ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemyPrefab (8).prefab");

            var itemConsumibleRef = so.FindProperty("itemConsumiblePrefab");
            if (itemConsumibleRef != null) itemConsumibleRef.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LifePrefab.prefab");

            var itemResourceRef = so.FindProperty("itemResourcePrefab");
            if (itemResourceRef != null) itemResourceRef.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/IronPrefab.prefab");

            var itemThingRef = so.FindProperty("itemThingPrefab");
            if (itemThingRef != null) itemThingRef.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ExperiencePrefab.prefab");

            var propMineralRef = so.FindProperty("propMineralPrefab");
            if (propMineralRef != null) propMineralRef.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MineralsPrefab.prefab");

            var propTreeRef = so.FindProperty("propTreePrefab");
            if (propTreeRef != null) propTreeRef.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TreePrefab.prefab");

            var propBuildingRef = so.FindProperty("propBuildingPrefab");
            if (propBuildingRef != null) propBuildingRef.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BuildingsPrefab.prefab");

            var propPlantRef = so.FindProperty("propPlantPrefab");
            if (propPlantRef != null) propPlantRef.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlantsPrefab.prefab");
        }
    }
}
