using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Combating.Scripts {

    #region Data Structures, Interfaces & Enums

    public enum UserType
    {
        XenoBot = 0
    }

    public enum HostType
    {
        Neutral = 0,
        Melee = 1,
        Range = 2,
        Hybrid = 3
    }

    public enum ItemType
    {
        Thing = 0,
        Resource = 1,
        Consumible = 2,
        Costume = 3
    }

    public enum PropType
    {
        Building = 0,
        Tree = 1,
        Plant = 2
    }

    public enum StatType
    {
        Health,
        Damage,
        Speed,
        Defense
    }

    public interface IBalanceCalibratable
    {
        void CalibrateStats(EntityStats stats);
        EntityStats CurrentStats { get; }
        event Action<EntityStats> OnStatsUpdated;
    }

    public interface ILootableEntity
    {
        ItemType LootCategory { get; }
        int LootQuantity { get; }
        int LootDropChance { get; }
        void DropLootOnDeactivate();
    }

    public interface IPoolRecyclable
    {
        void PrepareForSpawn(Vector3 position, Quaternion rotation);
        void RecycleEntity();
        event Action OnEntityRecycled;
    }

    public interface IBalanceProvider
    {
        EntityStats GetEntityBalance(GameObject entity);
        int DifficultyLevel { get; }
        int AggroMultiplier { get; }
        event Action<GameObject, EntityStats> OnEntityCalibrated;
        event Action<GameObject, ItemType, int> OnLootDropped;
        event Action<GameObject> OnEntityRecycled;
    }

    [System.Serializable]
    public struct UserSpawnPointData
    {
        public Transform pointTransform;
        public UserType playerTypeToSpawn;
        public GameObject customPrefab;
    }

    [System.Serializable]
    public struct HostSpawnPointData
    {
        public Transform pointTransform;
        public HostType enemyTypeToSpawn;
        public GameObject customPrefab;
    }

    [System.Serializable]
    public struct ItemSpawnPointData
    {
        public Transform pointTransform;
        public ItemType categoryToSpawn;
        public GameObject customPrefab;
    }

    [System.Serializable]
    public struct PropSpawnPointData
    {
        public Transform pointTransform;
        public PropType categoryToSpawn;
        public GameObject customPrefab;
    }

    [System.Serializable]
    public struct EntityStats
    {
        public int maxHealth;
        public int currentHealth;
        public int attackDamage;
        public int moveSpeed;
        public int defenseRatio;

        public int detectionRadius;
        public int shootRange;
        public int meleeRange;
        public int wanderRadius;

        public int maxAmmo;
        public int shieldCapacity;
        public int fuelCapacity;

        public int expReward;
        public int expToNextLevel;
        public int attackPerLevel;
        public int defensePerLevel;

        public ItemType lootItemType;
        public int lootQuantity;
        public int lootDropChance;

        public override string ToString()
        {
            return $"[HP: {maxHealth} | DMG: {attackDamage} | SPD: {moveSpeed} | DEF: {defenseRatio}% | DET: {detectionRadius} | SHOT: {shootRange} | EXP: {expReward} | LOOT: {lootItemType}x{lootQuantity}]";
        }
    }

    #endregion

    public class BalanceManager : MonoBehaviour, IBalanceProvider
    {
        public static BalanceManager Instance { get; private set; }

        public event Action<GameObject, EntityStats> OnEntityCalibrated;
        public event Action<GameObject, ItemType, int> OnLootDropped;
        public event Action<GameObject> OnEntityRecycled;

        [Header("Balance Parameters")]
        [Tooltip("Nivel o dificultad global de la escena actual.")]
        [Range(1, 100)] public int difficultyLevel = 1;

        [Tooltip("Factor global de agresividad de la escena.")]
        [Range(1, 10)] public int aggroMultiplier = 1;

        [Tooltip("Escala matemática de progresión (Crecimiento Entero).")]
        [Range(1, 10)] public int growthFactor = 2;

        public int DifficultyLevel => difficultyLevel;
        public int AggroMultiplier => aggroMultiplier;

        [Header("Spawn Setup")]
        [SerializeField] private List<UserSpawnPointData> userSpawnPoints = new List<UserSpawnPointData>();
        [SerializeField] private List<HostSpawnPointData> hostSpawnPoints = new List<HostSpawnPointData>();
        [SerializeField] private List<ItemSpawnPointData> itemSpawnPoints = new List<ItemSpawnPointData>();
        [SerializeField] private List<PropSpawnPointData> propSpawnPoints = new List<PropSpawnPointData>();

        [Header("Prefabs References - Hosts")]
        [SerializeField] private GameObject userPlayerPrefab;
        [SerializeField] private GameObject hostMeleePrefab;
        [SerializeField] private GameObject hostRangePrefab;
        [SerializeField] private GameObject hostHybridPrefab;

        [Header("Prefabs References - Items")]
        [SerializeField] private GameObject itemThingPrefab;
        [SerializeField] private GameObject itemResourcePrefab;
        [SerializeField] private GameObject itemConsumiblePrefab;
        [SerializeField] private GameObject itemCostumePrefab;

        [Header("Prefabs References - Props")]
        [SerializeField] private GameObject propBuildingPrefab;
        [SerializeField] private GameObject propTreePrefab;
        [SerializeField] private GameObject propMineralPrefab;
        [SerializeField] private GameObject propPlantPrefab;

        private const int MIN_BASE_HEALTH = 1;
        private const int MIN_BASE_DAMAGE = 1;
        private const int MIN_BASE_DEFENSE = 0;
        private const int MIN_BASE_SPEED = 1;

        private const int BASE_EXP_TO_LEVEL = 100;
        private const int BASE_DETECTION_RADIUS = 15;
        private const int BASE_SHOOT_RANGE = 20;
        private const int BASE_MELEE_RANGE = 3;
        private const int BASE_WANDER_RADIUS = 10;
        private const int BASE_MAX_AMMO = 30;
        private const int BASE_SHIELD_CAPACITY = 50;
        private const int BASE_FUEL_CAPACITY = 100;

        private const int DEFAULT_ITEM_SCALE = 1;
        private const int DEFAULT_PROP_HEALTH_MULT = 1;
        private const float DEFAULT_CALIBRATION_DISTANCE = 20.0f;

        [Header("Terrain & Navigation Setup")]
        [SerializeField] private NavMeshSurface navMeshSurface;
        [SerializeField] private float maxCalibrationDistance = DEFAULT_CALIBRATION_DISTANCE;

        private readonly Dictionary<string, Queue<GameObject>> _objectPools = new Dictionary<string, Queue<GameObject>>();
        private readonly Dictionary<int, string> _activeInstanceToKey = new Dictionary<int, string>();
        private Transform _poolParentTransform;

        private readonly Dictionary<GameObject, EntityStats> _activeStatsRegistry = new Dictionary<GameObject, EntityStats>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _poolParentTransform = new GameObject("[ObjectPool]").transform;
            _poolParentTransform.SetParent(transform);
        }

        private void Start()
        {
            InitializeSceneSpawns();
        }

        #region 1. Dynamic Generic Object Pool Pattern & Terrain Calibration

        public Vector3 CalibrateSpawnPosition(Vector3 rawPosition)
        {
            float maxDist = maxCalibrationDistance > 0f ? maxCalibrationDistance : DEFAULT_CALIBRATION_DISTANCE;

            if (TryRaycastSurface(rawPosition, Vector3.down, maxDist, out Vector3 calYDown)) return calYDown;
            if (TryRaycastSurface(rawPosition, Vector3.up, maxDist, out Vector3 calYUp)) return calYUp;

            if (TryRaycastSurface(rawPosition, Vector3.back, maxDist, out Vector3 calZBack)) return calZBack;
            if (TryRaycastSurface(rawPosition, Vector3.forward, maxDist, out Vector3 calZFwd)) return calZFwd;

            if (TryRaycastSurface(rawPosition, Vector3.right, maxDist, out Vector3 calXRight)) return calXRight;
            if (TryRaycastSurface(rawPosition, Vector3.left, maxDist, out Vector3 calXLeft)) return calXLeft;

            if (NavMesh.SamplePosition(rawPosition, out NavMeshHit navHit, maxDist, NavMesh.AllAreas))
            {
                return navHit.position;
            }

            return rawPosition;
        }

        private bool TryRaycastSurface(Vector3 origin, Vector3 direction, float distance, out Vector3 result)
        {
            result = origin;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, distance))
            {
                if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 2.0f, NavMesh.AllAreas))
                {
                    result = navHit.position;
                    return true;
                }
                result = hit.point;
                return true;
            }
            return false;
        }

        public GameObject SpawnFromPool(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;

            string poolKey = prefab.name;

            if (!_objectPools.ContainsKey(poolKey))
            {
                _objectPools[poolKey] = new Queue<GameObject>();
            }

            GameObject instance;

            if (_objectPools[poolKey].Count > 0)
            {
                instance = _objectPools[poolKey].Dequeue();
            }
            else
            {
                instance = Instantiate(prefab, _poolParentTransform);
                instance.name = prefab.name;
            }

            Vector3 calibratedPos = CalibrateSpawnPosition(position);
            instance.transform.SetPositionAndRotation(calibratedPos, rotation);
            instance.SetActive(true);

            if (instance.TryGetComponent<IPoolRecyclable>(out var recyclable))
            {
                recyclable.PrepareForSpawn(calibratedPos, rotation);
            }

            int instanceID = instance.GetInstanceID();
            _activeInstanceToKey[instanceID] = poolKey;

            return instance;
        }

        public Transform SpawnFromPool(Transform transformPrefab, Vector3 position, Quaternion rotation)
        {
            if (transformPrefab == null) return null;
            GameObject spawnedObj = SpawnFromPool(transformPrefab.gameObject, position, rotation);
            return spawnedObj != null ? spawnedObj.transform : null;
        }

        public GameObject SpawnItem(ItemType category, Vector3 position, Quaternion rotation)
        {
            GameObject prefab = GetPrefabForItemType(category);
            GameObject obj = SpawnFromPool(prefab, position, rotation);
            if (obj != null)
            {
                obj.transform.localScale = Vector3.one * DEFAULT_ITEM_SCALE;
            }
            return obj;
        }

        public Transform SpawnItemTransform(ItemType category, Vector3 position, Quaternion rotation)
        {
            GameObject itemObj = SpawnItem(category, position, rotation);
            return itemObj != null ? itemObj.transform : null;
        }

        public GameObject SpawnProp(PropType category, Vector3 position, Quaternion rotation)
        {
            GameObject prefab = GetPrefabForPropType(category);
            GameObject obj = SpawnFromPool(prefab, position, rotation);
            if (obj != null)
            {
                EntityStats propStats = CalculatePropStats(category);
                RegisterEntityStats(obj, propStats);
            }
            return obj;
        }

        public Transform SpawnPropTransform(PropType category, Vector3 position, Quaternion rotation)
        {
            GameObject propObj = SpawnProp(category, position, rotation);
            return propObj != null ? propObj.transform : null;
        }

        public void RecycleToPool(GameObject instance)
        {
            if (instance == null) return;

            if (instance.TryGetComponent<ILootableEntity>(out var lootable))
            {
                lootable.DropLootOnDeactivate();
            }
            else if (_activeStatsRegistry.TryGetValue(instance, out EntityStats stats))
            {
                if (stats.lootQuantity > 0 && UnityEngine.Random.Range(0, 100) < stats.lootDropChance)
                {
                    for (int l = 0; l < stats.lootQuantity; l++)
                    {
                        SpawnItem(stats.lootItemType, instance.transform.position, instance.transform.rotation);
                    }
                    OnLootDropped?.Invoke(instance, stats.lootItemType, stats.lootQuantity);
                }
            }

            if (instance.TryGetComponent<IPoolRecyclable>(out var recyclable))
            {
                recyclable.RecycleEntity();
            }

            OnEntityRecycled?.Invoke(instance);

            int instanceID = instance.GetInstanceID();

            if (_activeInstanceToKey.TryGetValue(instanceID, out string poolKey))
            {
                instance.SetActive(false);
                instance.transform.SetParent(_poolParentTransform);
                _objectPools[poolKey].Enqueue(instance);

                if (_activeStatsRegistry.ContainsKey(instance))
                {
                    _activeStatsRegistry.Remove(instance);
                }
            }
            else
            {
                Destroy(instance);
            }
        }

        public void RecycleToPool(Transform instanceTransform)
        {
            if (instanceTransform != null)
            {
                RecycleToPool(instanceTransform.gameObject);
            }
        }

        #endregion

        #region 2. Spawn Manager Pattern

        public void InitializeSceneSpawns()
        {
            int totalPlayers = userSpawnPoints.Count;
            for (int i = 0; i < totalPlayers; i++)
            {
                UserSpawnPointData playerData = userSpawnPoints[i];
                GameObject playerPrefabToUse = playerData.customPrefab != null ? playerData.customPrefab : userPlayerPrefab;

                if (playerPrefabToUse != null && playerData.pointTransform != null)
                {
                    GameObject player = SpawnFromPool(playerPrefabToUse, playerData.pointTransform.position, playerData.pointTransform.rotation);
                    EntityStats playerStats = CalculateUserStats(playerData.playerTypeToSpawn);
                    RegisterEntityStats(player, playerStats);
                }
            }

            int totalEnemies = hostSpawnPoints.Count;
            for (int i = 0; i < totalEnemies; i++)
            {
                HostSpawnPointData spawnData = hostSpawnPoints[i];
                GameObject enemyPrefabToUse = GetPrefabForEnemyType(spawnData.enemyTypeToSpawn);

                if (enemyPrefabToUse != null && spawnData.pointTransform != null)
                {
                    GameObject enemy = SpawnFromPool(enemyPrefabToUse, spawnData.pointTransform.position, spawnData.pointTransform.rotation);
                    EntityStats enemyStats = CalculateHostStats(spawnData.enemyTypeToSpawn, i, totalEnemies);
                    RegisterEntityStats(enemy, enemyStats);
                }
            }

            int totalItems = itemSpawnPoints.Count;
            for (int i = 0; i < totalItems; i++)
            {
                ItemSpawnPointData itemData = itemSpawnPoints[i];
                if (itemData.pointTransform != null)
                {
                    GameObject prefabToUse = itemData.customPrefab != null ? itemData.customPrefab : GetPrefabForItemType(itemData.categoryToSpawn);
                    if (prefabToUse != null)
                    {
                        SpawnFromPool(prefabToUse, itemData.pointTransform.position, itemData.pointTransform.rotation);
                    }
                }
            }

            int totalProps = propSpawnPoints.Count;
            for (int i = 0; i < totalProps; i++)
            {
                PropSpawnPointData propData = propSpawnPoints[i];
                if (propData.pointTransform != null)
                {
                    GameObject prefabToUse = propData.customPrefab != null ? propData.customPrefab : GetPrefabForPropType(propData.categoryToSpawn);
                    if (prefabToUse != null)
                    {
                        GameObject propObj = SpawnFromPool(prefabToUse, propData.pointTransform.position, propData.pointTransform.rotation);
                        EntityStats propStats = CalculatePropStats(propData.categoryToSpawn);
                        RegisterEntityStats(propObj, propStats);
                    }
                }
            }
        }

        private GameObject GetPrefabForEnemyType(HostType type)
        {
            return type switch
            {
                HostType.Melee => hostMeleePrefab,
                HostType.Range => hostRangePrefab,
                HostType.Hybrid => hostHybridPrefab,
                _ => hostMeleePrefab
            };
        }

        private GameObject GetPrefabForItemType(ItemType category)
        {
            return category switch
            {
                ItemType.Thing => itemThingPrefab,
                ItemType.Resource => itemResourcePrefab,
                ItemType.Consumible => itemConsumiblePrefab,
                ItemType.Costume => itemCostumePrefab,
                _ => itemThingPrefab
            };
        }

        private GameObject GetPrefabForPropType(PropType category)
        {
            return category switch
            {
                PropType.Building => propBuildingPrefab,
                PropType.Tree => propTreePrefab,
                PropType.Plant => propPlantPrefab,
                _ => propMineralPrefab
            };
        }

        #endregion

        #region 3. Dynamic Values Instantiator & Multilateral Balance Engine

        private int GetTotalEnemyThreatWeight()
        {
            int threat = 0;
            int totalEnemies = hostSpawnPoints.Count;
            for (int i = 0; i < totalEnemies; i++)
            {
                HostType type = hostSpawnPoints[i].enemyTypeToSpawn;
                threat += 1 + (int)type;
            }
            return Math.Max(MIN_BASE_HEALTH, threat * (1 + difficultyLevel) * aggroMultiplier);
        }

        private int GetTotalItemSupportCapacity()
        {
            int itemCount = Math.Max(1, itemSpawnPoints.Count);
            return 1 + itemCount;
        }

        public EntityStats CalculateHostStats(HostType enemyType, int spawnIndex, int totalSpawns)
        {
            int typeVal = (int)enemyType;
            int typeFactor = 1 + typeVal;
            int spatialFactor = totalSpawns > 1 ? (spawnIndex * 10 / (totalSpawns - 1)) : 10;

            int itemCompensation = 1 + itemSpawnPoints.Count;
            int powGrowth = 1;
            for (int p = 0; p < difficultyLevel; p++) powGrowth *= Math.Max(1, growthFactor);

            int health = Math.Max(MIN_BASE_HEALTH, MIN_BASE_HEALTH * typeFactor * powGrowth * itemCompensation * (10 + (spatialFactor * aggroMultiplier)) / 10);
            int damage = Math.Max(MIN_BASE_DAMAGE, MIN_BASE_DAMAGE * typeFactor * (1 + difficultyLevel) * aggroMultiplier);
            int speed = Math.Max(MIN_BASE_SPEED, MIN_BASE_SPEED + Math.Max(0, 4 - typeVal));
            int defense = Math.Min(80, Math.Max(MIN_BASE_DEFENSE, MIN_BASE_DEFENSE + (typeVal * 5) + (difficultyLevel * 2)));

            int detRadius = Math.Max(1, BASE_DETECTION_RADIUS + (typeVal * 5) + (difficultyLevel * 2));
            int sRange = enemyType == HostType.Range || enemyType == HostType.Hybrid ? Math.Max(1, BASE_SHOOT_RANGE + (typeVal * 3)) : 0;
            int mRange = enemyType == HostType.Melee || enemyType == HostType.Hybrid ? Math.Max(1, BASE_MELEE_RANGE + typeVal) : 0;
            int wRadius = Math.Max(1, BASE_WANDER_RADIUS + (typeVal * 2));

            int ammo = Math.Max(1, BASE_MAX_AMMO + (typeVal * 10));
            int shield = typeVal >= 2 ? Math.Max(1, BASE_SHIELD_CAPACITY + (typeVal * 15)) : 0;
            int fuel = Math.Max(1, BASE_FUEL_CAPACITY + (typeVal * 20));

            int exp = Math.Max(1, (10 + (typeVal * 15)) * (1 + difficultyLevel) * aggroMultiplier);
            int nextExp = Math.Max(1, BASE_EXP_TO_LEVEL * powGrowth);
            int atkPerLvl = Math.Max(1, 1 + typeVal);
            int defPerLvl = Math.Max(1, 1 + typeVal);

            ItemType lootType = (ItemType)(typeVal % 4);
            int lootQty = Math.Max(1, 1 + typeVal);
            int dropChance = Math.Min(100, 50 + (typeVal * 15));

            return new EntityStats
            {
                maxHealth = health,
                currentHealth = health,
                attackDamage = damage,
                moveSpeed = speed,
                defenseRatio = defense,
                detectionRadius = detRadius,
                shootRange = sRange,
                meleeRange = mRange,
                wanderRadius = wRadius,
                maxAmmo = ammo,
                shieldCapacity = shield,
                fuelCapacity = fuel,
                expReward = exp,
                expToNextLevel = nextExp,
                attackPerLevel = atkPerLvl,
                defensePerLevel = defPerLvl,
                lootItemType = lootType,
                lootQuantity = lootQty,
                lootDropChance = dropChance
            };
        }

        public EntityStats CalculateUserStats(UserType playerType)
        {
            int typeVal = (int)playerType;
            int userFactor = 1 + typeVal;
            int threatToItemsRatio = GetTotalEnemyThreatWeight() / GetTotalItemSupportCapacity();

            int powGrowth = 1;
            for (int p = 0; p < difficultyLevel; p++) powGrowth *= Math.Max(1, growthFactor);

            int health = Math.Max(MIN_BASE_HEALTH, MIN_BASE_HEALTH * userFactor * powGrowth * (1 + threatToItemsRatio));
            int damage = Math.Max(MIN_BASE_DAMAGE, MIN_BASE_DAMAGE * userFactor * (1 + difficultyLevel) * aggroMultiplier);
            int speed = Math.Max(MIN_BASE_SPEED, MIN_BASE_SPEED + 6);
            int defense = Math.Min(85, Math.Max(MIN_BASE_DEFENSE, MIN_BASE_DEFENSE + (typeVal * 5) + (difficultyLevel * 2)));

            int detRadius = Math.Max(1, BASE_DETECTION_RADIUS * 2);
            int sRange = Math.Max(1, BASE_SHOOT_RANGE * 2);
            int mRange = Math.Max(1, BASE_MELEE_RANGE + userFactor);
            int wRadius = 0;

            int ammo = Math.Max(1, BASE_MAX_AMMO * userFactor);
            int shield = Math.Max(1, BASE_SHIELD_CAPACITY * userFactor);
            int fuel = Math.Max(1, BASE_FUEL_CAPACITY * userFactor);

            int nextExp = Math.Max(1, BASE_EXP_TO_LEVEL * growthFactor);
            int atkPerLvl = Math.Max(1, 2 + typeVal);
            int defPerLvl = Math.Max(1, 2 + typeVal);

            return new EntityStats
            {
                maxHealth = health,
                currentHealth = health,
                attackDamage = damage,
                moveSpeed = speed,
                defenseRatio = defense,
                detectionRadius = detRadius,
                shootRange = sRange,
                meleeRange = mRange,
                wanderRadius = wRadius,
                maxAmmo = ammo,
                shieldCapacity = shield,
                fuelCapacity = fuel,
                expReward = 0,
                expToNextLevel = nextExp,
                attackPerLevel = atkPerLvl,
                defensePerLevel = defPerLvl,
                lootItemType = ItemType.Thing,
                lootQuantity = 0,
                lootDropChance = 0
            };
        }

        public int CalculateItemValue(ItemType category)
        {
            int totalThreat = GetTotalEnemyThreatWeight();
            int itemCount = Math.Max(1, itemSpawnPoints.Count);
            int baseItemVal = (totalThreat / itemCount) * MIN_BASE_HEALTH;
            int categoryFactor = 1 + (int)category;
            return Math.Max(1, baseItemVal * categoryFactor);
        }

        public EntityStats CalculatePropStats(PropType category)
        {
            int propVal = (int)category;
            int propFactor = 1 + propVal;
            int sceneThreat = GetTotalEnemyThreatWeight();

            int health = Math.Max(MIN_BASE_HEALTH, MIN_BASE_HEALTH * propFactor * DEFAULT_PROP_HEALTH_MULT * Math.Max(1, sceneThreat));
            ItemType propLootType = category == PropType.Tree ? ItemType.Resource : ItemType.Thing;

            return new EntityStats
            {
                maxHealth = health,
                currentHealth = health,
                attackDamage = 0,
                moveSpeed = 0,
                defenseRatio = 0,
                detectionRadius = 0,
                shootRange = 0,
                meleeRange = 0,
                wanderRadius = 0,
                maxAmmo = 0,
                shieldCapacity = 0,
                fuelCapacity = 0,
                expReward = propFactor * 5,
                expToNextLevel = 0,
                attackPerLevel = 0,
                defensePerLevel = 0,
                lootItemType = propLootType,
                lootQuantity = Math.Max(1, propFactor),
                lootDropChance = 100
            };
        }

        private void RegisterEntityStats(GameObject entity, EntityStats stats)
        {
            _activeStatsRegistry[entity] = stats;
            if (entity.TryGetComponent<IBalanceCalibratable>(out var calibratable))
            {
                calibratable.CalibrateStats(stats);
            }
            OnEntityCalibrated?.Invoke(entity, stats);
        }

        public EntityStats? GetEntityStats(GameObject entity)
        {
            if (_activeStatsRegistry.TryGetValue(entity, out EntityStats stats))
            {
                return stats;
            }
            return null;
        }

        public EntityStats GetEntityBalance(GameObject entity)
        {
            return GetEntityStats(entity) ?? default;
        }

        #endregion
    }
}