using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Combating.Scripts {

    #region Data Structures & Enums

    public enum UserType
    {
        Novice = 0,
        Warrior = 1,
        Mage = 2,
        Tank = 3
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
        Ability = 2,
        Costume = 3
    }

    public enum PropType
    {
        Building = 0,
        Tree = 1,
        Rock = 2
    }

    public enum StatType
    {
        Health,
        Damage,
        Speed,
        Defense
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
        public float maxHealth;
        public float currentHealth;
        public float attackDamage;
        public float moveSpeed;
        public float defenseRatio;

        public override string ToString()
        {
            return $"[HP: {maxHealth:F1} | DMG: {attackDamage:F1} | SPD: {moveSpeed:F1} | DEF: {defenseRatio:F2}]";
        }
    }

    #endregion

    public class BalanceManager : MonoBehaviour
    {
        public static BalanceManager Instance { get; private set; }

        [Header("Balance Parameters")]
        [Tooltip("Nivel o dificultad global de la escena actual.")]
        [Range(1, 100)] public int difficultyLevel = 1;

        [Tooltip("Factor global de agresividad de la escena.")]
        [Range(0.5f, 3.0f)] public float aggroMultiplier = 1.0f;

        [Tooltip("Escala matemática de progresión (Crecimiento Exponencial/Logarítmico).")]
        public float growthFactor = 1.15f;

        [Header("Spawn Setup")]
        [SerializeField] private List<HostSpawnPointData> enemySpawnPoints = new List<HostSpawnPointData>();
        [SerializeField] private List<ItemSpawnPointData> itemSpawnPoints = new List<ItemSpawnPointData>();
        [SerializeField] private List<PropSpawnPointData> propSpawnPoints = new List<PropSpawnPointData>();
        [SerializeField] private Transform playerSpawnPoint;

        [Header("Prefabs References - Characters")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject enemyMeleePrefab;
        [SerializeField] private GameObject enemyRangePrefab;
        [SerializeField] private GameObject enemyHybridPrefab;

        [Header("Prefabs References - Items")]
        [SerializeField] private GameObject itemThingPrefab;
        [SerializeField] private GameObject itemResourcePrefab;
        [SerializeField] private GameObject itemAbilityPrefab;
        [SerializeField] private GameObject itemCostumePrefab;

        [Header("Prefabs References - Props & Environment")]
        [SerializeField] private GameObject buildingPrefab;
        [SerializeField] private GameObject treePrefab;
        [SerializeField] private GameObject rockPrefab;
        [SerializeField] private GameObject propPrefab;

        [Header("Optional Overrides")]
        public Optional<float> customItemSpawnScale;
        public Optional<float> customPropHealthMultiplier;

        private const float MIN_BASE_HEALTH = 1.0f;
        private const float MIN_BASE_DAMAGE = 1.0f;
        private const float MIN_BASE_DEFENSE = 0.0f;
        private const float MIN_BASE_SPEED = 1.0f;

        private const float DEFAULT_ITEM_SCALE = 1.0f;
        private const float DEFAULT_PROP_HEALTH_MULT = 1.0f;

        public float EffectiveItemScale => customItemSpawnScale.GetValue(DEFAULT_ITEM_SCALE);
        public float EffectivePropHealthMult => customPropHealthMultiplier.GetValue(DEFAULT_PROP_HEALTH_MULT);

        // --- Dynamic Generic Object Pool ---
        private readonly Dictionary<string, Queue<GameObject>> _objectPools = new Dictionary<string, Queue<GameObject>>();
        private readonly Dictionary<int, string> _activeInstanceToKey = new Dictionary<int, string>();
        private Transform _poolParentTransform;

        // --- Stat Registries ---
        private readonly Dictionary<GameObject, EntityStats> _activeStatsRegistry = new Dictionary<GameObject, EntityStats>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _poolParentTransform = new GameObject("[Dynamic_ObjectPool_Holder]").transform;
            _poolParentTransform.SetParent(transform);
        }

        private void Start()
        {
            InitializeSceneSpawns();
        }

        #region 1. Dynamic Generic Object Pool Pattern

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

            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);

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
                float scale = EffectiveItemScale;
                obj.transform.localScale = Vector3.one * scale;
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
            if (playerPrefab != null && playerSpawnPoint != null)
            {
                GameObject player = SpawnFromPool(playerPrefab, playerSpawnPoint.position, playerSpawnPoint.rotation);
                EntityStats playerStats = CalculateUserStats(UserType.Warrior);
                RegisterEntityStats(player, playerStats);
            }

            int totalEnemies = enemySpawnPoints.Count;
            for (int i = 0; i < totalEnemies; i++)
            {
                HostSpawnPointData spawnData = enemySpawnPoints[i];
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
                HostType.Melee => enemyMeleePrefab,
                HostType.Range => enemyRangePrefab,
                HostType.Hybrid => enemyHybridPrefab,
                _ => enemyMeleePrefab
            };
        }

        private GameObject GetPrefabForItemType(ItemType category)
        {
            return category switch
            {
                ItemType.Thing => itemThingPrefab,
                ItemType.Resource => itemResourcePrefab,
                ItemType.Ability => itemAbilityPrefab,
                ItemType.Costume => itemCostumePrefab,
                _ => itemThingPrefab
            };
        }

        private GameObject GetPrefabForPropType(PropType category)
        {
            return category switch
            {
                PropType.Building => buildingPrefab,
                PropType.Tree => treePrefab,
                PropType.Rock => rockPrefab,
                _ => propPrefab
            };
        }

        #endregion

        #region 3. Dynamic Values Instantiator & Multilateral Balance Engine

        private float GetTotalEnemyThreatWeight()
        {
            float threat = 0f;
            int totalEnemies = enemySpawnPoints.Count;
            for (int i = 0; i < totalEnemies; i++)
            {
                HostType type = enemySpawnPoints[i].enemyTypeToSpawn;
                threat += (1.0f + (int)type * 0.5f);
            }
            return Mathf.Max(MIN_BASE_HEALTH, threat * (1.0f + difficultyLevel * 0.1f) * aggroMultiplier);
        }

        private float GetTotalItemSupportCapacity()
        {
            int itemCount = Mathf.Max(1, itemSpawnPoints.Count);
            return 1.0f + (itemCount * 0.1f);
        }

        public EntityStats CalculateHostStats(HostType enemyType, int spawnIndex, int totalSpawns)
        {
            int typeVal = (int)enemyType;
            float typeFactor = 1.0f + (typeVal * 0.5f);
            float spatialFactor = totalSpawns > 1 ? (float)spawnIndex / (totalSpawns - 1) : 1f;

            float itemCompensation = 1.0f + (itemSpawnPoints.Count * 0.05f);
            float scaledHealth = MIN_BASE_HEALTH * typeFactor * Mathf.Pow(growthFactor, difficultyLevel) * itemCompensation * (1f + (spatialFactor * 0.2f * aggroMultiplier));
            float scaledDamage = MIN_BASE_DAMAGE * typeFactor * (1f + (difficultyLevel * 0.1f)) * aggroMultiplier;
            float defenseRatio = Mathf.Clamp(MIN_BASE_DEFENSE + (typeVal * 0.05f) + Mathf.Log(difficultyLevel + 1) * 0.08f, MIN_BASE_DEFENSE, 0.80f);
            float moveSpeed = MIN_BASE_SPEED + Math.Max(0f, 4.0f - (typeVal * 0.5f));

            return new EntityStats
            {
                maxHealth = scaledHealth,
                currentHealth = scaledHealth,
                attackDamage = scaledDamage,
                moveSpeed = moveSpeed,
                defenseRatio = defenseRatio
            };
        }

        public EntityStats CalculateUserStats(UserType playerType)
        {
            int typeVal = (int)playerType;
            float userFactor = 1.0f + (typeVal * 0.5f);
            float threatToItemsRatio = GetTotalEnemyThreatWeight() / GetTotalItemSupportCapacity();

            float scaledHealth = MIN_BASE_HEALTH * userFactor * Mathf.Pow(growthFactor, difficultyLevel * 0.8f) * (1.0f + threatToItemsRatio * 0.15f);
            float scaledDamage = MIN_BASE_DAMAGE * userFactor * (1.0f + (difficultyLevel * 0.12f)) * aggroMultiplier;
            float defenseRatio = Mathf.Clamp(MIN_BASE_DEFENSE + (typeVal * 0.05f) + Mathf.Log(difficultyLevel + 1) * 0.1f, MIN_BASE_DEFENSE, 0.85f);
            float moveSpeed = MIN_BASE_SPEED + 6.0f;

            return new EntityStats
            {
                maxHealth = scaledHealth,
                currentHealth = scaledHealth,
                attackDamage = scaledDamage,
                moveSpeed = moveSpeed,
                defenseRatio = defenseRatio
            };
        }

        public float CalculateItemValue(ItemType category)
        {
            float totalThreat = GetTotalEnemyThreatWeight();
            int itemCount = Mathf.Max(1, itemSpawnPoints.Count);
            float baseItemVal = (totalThreat / itemCount) * MIN_BASE_HEALTH;
            float categoryFactor = 1.0f + ((int)category * 0.25f);
            return baseItemVal * categoryFactor;
        }

        public EntityStats CalculatePropStats(PropType category)
        {
            int propVal = (int)category;
            float propFactor = 1.0f + (propVal * 1.0f);
            float sceneThreat = GetTotalEnemyThreatWeight();

            float scaledHp = MIN_BASE_HEALTH * propFactor * EffectivePropHealthMult * Mathf.Max(1.0f, sceneThreat * 0.5f);

            return new EntityStats
            {
                maxHealth = scaledHp,
                currentHealth = scaledHp,
                attackDamage = MIN_BASE_DAMAGE - 1.0f,
                moveSpeed = MIN_BASE_SPEED - 1.0f,
                defenseRatio = MIN_BASE_DEFENSE + 0.1f
            };
        }

        private void RegisterEntityStats(GameObject entity, EntityStats stats)
        {
            _activeStatsRegistry[entity] = stats;
        }

        public EntityStats? GetEntityStats(GameObject entity)
        {
            if (_activeStatsRegistry.TryGetValue(entity, out EntityStats stats))
            {
                return stats;
            }
            return null;
        }

        #endregion
    }
}