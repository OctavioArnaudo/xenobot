using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Combating.Scripts {

    #region Data Structures & Enums

    public enum PlayerType
    {
        Novice = 0,
        Warrior = 1,
        Mage = 2,
        Tank = 3
    }

    public enum EnemyType
    {
        Neutral = 0,
        Melee = 1,
        Range = 2,
        Hybrid = 3
    }

    public enum DynamicStatType
    {
        Health,
        Damage,
        Speed,
        Defense
    }

    [System.Serializable]
    public struct SpawnPointData
    {
        public Transform pointTransform;
        public EnemyType enemyTypeToSpawn;
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

        [Header("Scene Balance Parameters")]
        [Tooltip("Nivel o dificultad global de la escena actual.")]
        [Range(1, 100)] public int sceneDifficultyLevel = 1;

        [Tooltip("Factor global de agresividad de la escena.")]
        [Range(0.5f, 3.0f)] public float globalAggroMultiplier = 1.0f;

        [Tooltip("Escala matemática de progresión (Crecimiento Exponencial/Logarítmico).")]
        public float growthFactor = 1.15f;

        [Header("Spawn Points Setup")]
        [SerializeField] private List<SpawnPointData> enemySpawnPoints = new List<SpawnPointData>();
        [SerializeField] private Transform playerSpawnPoint;

        [Header("Prefabs References")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject enemyMeleePrefab;
        [SerializeField] private GameObject enemyRangePrefab;
        [SerializeField] private GameObject enemyHybridPrefab;

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
            // Carga inicial y spawneo de la escena respetando el balance matemático
            InitializeSceneSpawns();
        }

        #region 1. Dynamic Generic Object Pool Pattern

        /// <summary>
        /// Obtiene o instancia dinámicamente un GameObject reutilizable de la pool.
        /// </summary>
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

        /// <summary>
        /// Desactiva y retorna cualquier GameObject dinámico a la pool correspondiente.
        /// </summary>
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

        #endregion

        #region 2. Spawn Manager Pattern

        /// <summary>
        /// Carga la escena instanciando y activando los objetos en sus respectivos Spawn Points.
        /// </summary>
        public void InitializeSceneSpawns()
        {
            // 1. Spawnea al Player en su punto estratégico
            if (playerPrefab != null && playerSpawnPoint != null)
            {
                GameObject player = SpawnFromPool(playerPrefab, playerSpawnPoint.position, playerSpawnPoint.rotation);
                EntityStats playerStats = CalculatePlayerStats(PlayerType.Warrior);
                RegisterEntityStats(player, playerStats);
            }

            // 2. Spawnea los Enemies según los SpawnPoints configurados
            int totalEnemies = enemySpawnPoints.Count;
            for (int i = 0; i < totalEnemies; i++)
            {
                SpawnPointData spawnData = enemySpawnPoints[i];
                GameObject enemyPrefabToUse = GetPrefabForEnemyType(spawnData.enemyTypeToSpawn);

                if (enemyPrefabToUse != null && spawnData.pointTransform != null)
                {
                    GameObject enemy = SpawnFromPool(enemyPrefabToUse, spawnData.pointTransform.position, spawnData.pointTransform.rotation);

                    // Pondera el balance individual según el índice relativo del spawn point en la escena
                    EntityStats enemyStats = CalculateEnemyStats(spawnData.enemyTypeToSpawn, i, totalEnemies);
                    RegisterEntityStats(enemy, enemyStats);
                }
            }
        }

        private GameObject GetPrefabForEnemyType(EnemyType type)
        {
            return type switch
            {
                EnemyType.Melee => enemyMeleePrefab,
                EnemyType.Range => enemyRangePrefab,
                EnemyType.Hybrid => enemyHybridPrefab,
                _ => enemyMeleePrefab
            };
        }

        #endregion

        #region 3. Dynamic Values Instantiator & Mathematical Balance Engine

        /// <summary>
        /// Interrelaciona los valores base de los Enums con la dificultad global mediante ecuaciones matemáticas.
        /// </summary>
        public EntityStats CalculateEnemyStats(EnemyType enemyType, int spawnIndex, int totalSpawns)
        {
            // Extracción de base entera basada en Enum
            int baseTypeVal = (int)enemyType;

            // Formula 1: Base Stat derivada directamente del enum
            float baseHealth = 50f + (baseTypeVal * 25f);
            float baseDamage = 10f + (baseTypeVal * 7.5f);
            float baseSpeed = 5f - (baseTypeVal * 0.5f); // Tipos más pesados son más lentos

            // Formula 2: Factor de Posicionamiento Espacial en la Escena (0.0 a 1.0)
            float spatialFactor = totalSpawns > 1 ? (float)spawnIndex / (totalSpawns - 1) : 1f;

            // Formula 3: Interrelación de Dificultad Exponencial y Posicionamiento
            // HP = Base * (Growth ^ Difficulty) * (1 + SpatialFactor * AggressiveMultiplier)
            float scaledHealth = baseHealth * Mathf.Pow(growthFactor, sceneDifficultyLevel) * (1f + (spatialFactor * 0.2f * globalAggroMultiplier));

            // Damage = Base * (Difficulty * 0.8) * Aggro
            float scaledDamage = baseDamage * (1f + (sceneDifficultyLevel * 0.12f)) * globalAggroMultiplier;

            // Defense Ratio = Logarítmico para asintótica saturación (Max ~80% reducción)
            float defenseRatio = Mathf.Clamp(Mathf.Log(sceneDifficultyLevel + baseTypeVal + 1) * 0.15f, 0.05f, 0.80f);

            return new EntityStats
            {
                maxHealth = scaledHealth,
                currentHealth = scaledHealth,
                attackDamage = scaledDamage,
                moveSpeed = baseSpeed,
                defenseRatio = defenseRatio
            };
        }

        /// <summary>
        /// Calcula el balance matemático equivalente para los jugadores.
        /// </summary>
        public EntityStats CalculatePlayerStats(PlayerType playerType)
        {
            int baseTypeVal = (int)playerType;

            // Fórmula simétrica para mantener equilibrio matemático con la escena
            float baseHealth = 100f + (baseTypeVal * 40f);
            float baseDamage = 20f + (baseTypeVal * 15f);

            float scaledHealth = baseHealth * Mathf.Pow(growthFactor, sceneDifficultyLevel * 0.8f);
            float scaledDamage = baseDamage * (1f + (sceneDifficultyLevel * 0.15f));
            float defenseRatio = Mathf.Clamp(Mathf.Log(sceneDifficultyLevel + baseTypeVal + 2) * 0.18f, 0.1f, 0.85f);

            return new EntityStats
            {
                maxHealth = scaledHealth,
                currentHealth = scaledHealth,
                attackDamage = scaledDamage,
                moveSpeed = 7.0f,
                defenseRatio = defenseRatio
            };
        }

        private void RegisterEntityStats(GameObject entity, EntityStats stats)
        {
            _activeStatsRegistry[entity] = stats;
            Debug.Log($"[BalanceManager] Spawned '{entity.name}' Stats: {stats}");
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