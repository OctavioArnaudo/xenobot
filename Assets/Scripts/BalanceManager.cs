using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Runtime.Scripts.Singleton;
using Runtime.Scripts.Enemy;
using Runtime.Scripts.Pickup;
using Runtime.Scripts.Projectile;
using Runtime.Scripts.Player;
using Runtime.Scripts.Optional;

namespace Runtime.Scripts.Balance
{

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
        Plant = 2,
        Mineral = 3
    }

    public interface IBalanceCalibratable
    {
        void CalibrateStats(StatSpawnPointData stats);
        StatSpawnPointData CurrentStats { get; }
        event Action<StatSpawnPointData> OnStatsUpdated;
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
        StatSpawnPointData GetEntityBalance(GameObject entity);
        int DifficultyLevel { get; }
        int AggroMultiplier { get; }
        event Action<GameObject, StatSpawnPointData> OnEntityCalibrated;
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
    public struct StatSpawnPointData
    {
        // Estadísticas Base
        public Optional<int> maxHealth;
        public Optional<int> currentHealth;
        public Optional<int> attackDamage;
        public Optional<int> moveSpeed;
        public Optional<int> defenseRatio;

        // Rangos y Detección
        public Optional<int> detectionRadius;
        public Optional<int> shootRange;
        public Optional<int> meleeRange;
        public Optional<int> wanderRadius;

        // Capacidades de Combate y Soporte
        public Optional<int> maxAmmo;
        public Optional<int> shieldCapacity;
        public Optional<int> fuelCapacity;

        // Progresión y Experiencia
        public Optional<int> expReward;
        public Optional<int> expToNextLevel;
        public Optional<int> attackPerLevel;
        public Optional<int> defensePerLevel;

        // Sistema de Botín
        public ItemType lootItemType;
        public Optional<int> lootQuantity;
        public Optional<int> lootDropChance;

        // IA y Resistencias de Enemigos
        public Optional<int> attackCooldown;
        public Optional<int> physicalResistance;
        public Optional<int> energyResistance;
        public Optional<int> explosiveResistance;
        public Optional<int> retreatRadius;
        public Optional<int> retreatHealthThreshold;
        public Optional<int> specialSkillChance;

        // Movimiento Avanzado, Vuelo y Saltos (Solo Enteros)
        public Optional<int> jumpHeight;
        public Optional<int> maxJumps;
        public Optional<int> hasJetpack;              // 0 = Falso, 1 = Verdadero
        public Optional<int> jetpackConsumptionRate;
        public Optional<int> jetpackRechargeRate;

        // Tipos de Ataques Avanzados
        public Optional<int> smashDamage;
        public Optional<int> smashRadius;
        public Optional<int> shootFireRate;
        public Optional<int> shootSpread;

        // Mecánicas de Escudo Avanzadas
        public Optional<int> shieldRegenRate;
        public Optional<int> shieldRechargeDelay;
        public Optional<int> shieldBlockAngle;

        // Restauración de Recursos (Ítems Específicos)
        public Optional<int> healthRestoreAmount;
        public Optional<int> fuelRestoreAmount;
        public Optional<int> shieldRestoreAmount;
        public Optional<int> ammoRestoreAmount;
        public Optional<int> effectMagnitude;
        public Optional<int> itemRarity;

        // Propiedades de Elementos del Entorno (Props)
        public Optional<int> customLootType;
        public Optional<int> destructionDamage;
        public Optional<int> destructionRadius;
        public Optional<int> requiredDamageType;

        // Control de Ráfaga, Pausas de Disparo y Recalibración de Objetivo
        public Optional<int> burstShotsCapacity;
        public Optional<int> burstCooldownDuration;
        public Optional<int> targetRecalibrationDelay;
        public Optional<int> maxContinuousFireDuration;

    }

    #endregion

    public class BalanceManager : CSingletonMB<BalanceManager>, IBalanceProvider
    {

        public event Action<GameObject, StatSpawnPointData> OnEntityCalibrated;
        public event Action<GameObject, ItemType, int> OnLootDropped;
        public event Action<GameObject> OnEntityRecycled;

        // --- MOTOR DE BALANCE Y MÉTRICAS DE PACING PRIVADAS E INTERNAS (Deterministas y No Sobrescribibles) ---
        private const int DIFFICULTY_LEVEL = 1;
        private const int AGGRO_MULTIPLIER = 1;
        private const int GROWTH_FACTOR = 2;
        private const int COMBAT_PACING_MULTIPLIER = 3;
        private const int HEALTH_SCALING_DIVIDER = 3;

        private const int TARGET_MIN_HITS_TO_KILL = 2;
        private const int TARGET_MAX_BOSS_HITS = 20;
        private const int MIN_FINAL_BATTLE_DURATION_SECONDS = 30;
        private const int MAX_FINAL_BATTLE_DURATION_SECONDS = 120;
        private const int INDIVIDUAL_BATTLE_DURATION_SECONDS = 12;
        private const int SPEEDRUN_BATTLE_DURATION_SECONDS = 5;

        public int DifficultyLevel => DIFFICULTY_LEVEL;
        public int AggroMultiplier => AGGRO_MULTIPLIER;

        [Header("Spawn Setup")]
        [SerializeField] private List<UserSpawnPointData> userSpawnPoints = new List<UserSpawnPointData>();
        [SerializeField] private List<HostSpawnPointData> hostSpawnPoints = new List<HostSpawnPointData>();
        [SerializeField] private List<ItemSpawnPointData> itemSpawnPoints = new List<ItemSpawnPointData>();
        [SerializeField] private List<PropSpawnPointData> propSpawnPoints = new List<PropSpawnPointData>();

        [Header("Prefabs References - Users")]
        [SerializeField] private GameObject userPlayerPrefab;

        [Header("Prefabs References - Hosts")]
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

        // --- CRITERIOS FUNDAMENTADOS DE BALANCE DE COMBATE ---
        // 1. PLAYER_MIN_SURVIVAL_HEALTH (250 HP): Garantiza un Time-To-Kill (TTK) equilibrado donde el jugador
        //    puede resistir 10-15 impactos estándar de enemigos (con daño promedio de 15-25),
        //    dando margen táctico para usar dash, cobertura e ítems curativos.
        private const int PLAYER_MIN_SURVIVAL_HEALTH = 250;

        // 2. ENEMY_MIN_TACTICAL_HEALTH (40 HP): Asegura que un enemigo básico caiga en 2-4 impactos
        //    de un arma estándar del jugador (daño 15-25), evitando enemigos "esponja de balas"
        //    y manteniendo un ritmo de combate rápido y ágil.
        private const int ENEMY_MIN_TACTICAL_HEALTH = 40;

        // 3. MIN_BASE_DAMAGE (10 Daño): Establece una penalización real por error táctico.
        //    El daño enemigo debe ser lo suficientemente significativo para exigir atención,
        //    evitando combates triviales donde el daño es insignificante.
        private const int MIN_BASE_DAMAGE = 10;

        // 4. MIN_BASE_SPEED (5 Velocidad): Asegura que la movilidad básica permita la maniobrabilidad
        //    y técnicas de kiting frente a la IA enemiga.
        private const int MIN_BASE_SPEED = 5;

        // 5. PROP_MIN_DURABILITY (50 HP): Garantiza que los elementos del entorno (árboles, edificios, minerales)
        //    tengan una resistencia lógica que soporte múltiples golpes antes de destruirse.
        private const int PROP_MIN_DURABILITY = 50;
        private const int MIN_BASE_DEFENSE = 0;

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

        private readonly Dictionary<string, Queue<GameObject>> _objectPools = new Dictionary<string, Queue<GameObject>>();
        private readonly Dictionary<int, string> _activeInstanceToKey = new Dictionary<int, string>();
        private Transform _poolParentTransform;

        private readonly Dictionary<GameObject, StatSpawnPointData> _activeStatsRegistry = new Dictionary<GameObject, StatSpawnPointData>();
        private readonly HashSet<GameObject> _assignedSceneObjects = new HashSet<GameObject>();

        protected override void Awake()
        {
            base.Awake();

            _poolParentTransform = new GameObject("[ObjectPool]").transform;
            _poolParentTransform.SetParent(transform);
        }

        private void OnEnable()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnNetworkClientConnected;
            }
        }

        private void OnDisable()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnNetworkClientConnected;
            }
        }

        private void Start()
        {
            InitializeSceneSpawns();
        }

        private void Update()
        {
            CheckAndRegisterUnregisteredPlayers();
        }

        private void OnNetworkClientConnected(ulong clientId)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
                {
                    RegisterNetworkPlayer(client.PlayerObject.gameObject);
                }
            }
        }

        public void RegisterNetworkPlayer(GameObject playerObject, UserType userType = UserType.XenoBot)
        {
            if (playerObject == null) return;
            if (!_activeStatsRegistry.ContainsKey(playerObject))
            {
                StatSpawnPointData playerStats = CalculateUserStats(userType);
                RegisterStatSpawnPointData(playerObject, playerStats);
            }
        }

        private void CheckAndRegisterUnregisteredPlayers()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            for (int i = 0; i < players.Length; i++)
            {
                GameObject p = players[i];
                if (p != null && p.activeInHierarchy && !_activeStatsRegistry.ContainsKey(p))
                {
                    RegisterNetworkPlayer(p);
                }
            }
        }

        #region 1. Object Pool & Calibration

        private float CalculateCalibrationDistance()
        {
            return DEFAULT_CALIBRATION_DISTANCE + (DIFFICULTY_LEVEL * 2.0f);
        }

        public float GetVerticalGroundOffset(GameObject entity)
        {
            if (entity == null) return 0f;

            if (entity.TryGetComponent<CharacterController>(out var cc))
            {
                float bottomOffset = cc.center.y - (cc.height * 0.5f);
                if (bottomOffset < 0f) return -bottomOffset;
            }
            else if (entity.TryGetComponent<CapsuleCollider>(out var cap))
            {
                float bottomOffset = cap.center.y - (cap.height * 0.5f);
                if (bottomOffset < 0f) return -bottomOffset;
            }
            else if (entity.TryGetComponent<BoxCollider>(out var box))
            {
                float bottomOffset = box.center.y - (box.size.y * 0.5f);
                if (bottomOffset < 0f) return -bottomOffset;
            }
            else if (entity.TryGetComponent<SphereCollider>(out var sphere))
            {
                float bottomOffset = sphere.center.y - sphere.radius;
                if (bottomOffset < 0f) return -bottomOffset;
            }

            Renderer r = entity.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                float extentsY = r.bounds.extents.y;
                float pivotToCenterY = r.bounds.center.y - entity.transform.position.y;
                float bottomRelPivot = pivotToCenterY - extentsY;
                if (bottomRelPivot < 0f) return -bottomRelPivot;
            }

            return 0.4f;
        }

        public Vector3 CalibrateSpawnPosition(Vector3 rawPosition, GameObject entity = null)
        {
            if (entity != null && entity.GetComponent<ProjectileController>() != null)
            {
                return rawPosition;
            }

            float maxDist = CalculateCalibrationDistance();
            Vector3 surfacePos = rawPosition;

            if (TryRaycastSurface(rawPosition, Vector3.down, maxDist, out Vector3 calYDown)) surfacePos = calYDown;
            else if (TryRaycastSurface(rawPosition, Vector3.up, maxDist, out Vector3 calYUp)) surfacePos = calYUp;
            else if (TryRaycastSurface(rawPosition, Vector3.back, maxDist, out Vector3 calZBack)) surfacePos = calZBack;
            else if (TryRaycastSurface(rawPosition, Vector3.forward, maxDist, out Vector3 calZFwd)) surfacePos = calZFwd;
            else if (TryRaycastSurface(rawPosition, Vector3.right, maxDist, out Vector3 calXRight)) surfacePos = calXRight;
            else if (TryRaycastSurface(rawPosition, Vector3.left, maxDist, out Vector3 calXLeft)) surfacePos = calXLeft;
            else if (NavMesh.SamplePosition(rawPosition, out NavMeshHit navHit, maxDist, NavMesh.AllAreas)) surfacePos = navHit.position;

            if (entity != null)
            {
                float verticalOffset = GetVerticalGroundOffset(entity);
                if (entity.GetComponent<ILootableEntity>() != null || entity.GetComponent<PickupController>() != null)
                {
                    verticalOffset += 0.35f;
                }
                return surfacePos + Vector3.up * verticalOffset;
            }

            return surfacePos;
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

            string poolKey = $"{prefab.GetInstanceID()}_{prefab.name}";

            if (!_objectPools.ContainsKey(poolKey))
            {
                _objectPools[poolKey] = new Queue<GameObject>();
            }

            GameObject instance = null;

            while (_objectPools[poolKey].Count > 0)
            {
                instance = _objectPools[poolKey].Dequeue();
                if (instance != null) break;
            }

            if (instance == null)
            {
                instance = Instantiate(prefab, position, rotation, _poolParentTransform);   // nace en el punto de spawn: el NavMeshAgent encuentra NavMesh (sin warnings)
                instance.name = prefab.name;
            }

            Vector3 calibratedPos = CalibrateSpawnPosition(position, instance);
            instance.transform.SetPositionAndRotation(calibratedPos, rotation);
            instance.SetActive(true);

            if (instance.TryGetComponent<IPoolRecyclable>(out var recyclable))
            {
                recyclable.PrepareForSpawn(calibratedPos, rotation);
            }

            if (instance.TryGetComponent<NetworkObject>(out var netComp))
            {
                netComp.enabled = true;
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

        public GameObject SpawnProp(PropType category, Vector3 position, Quaternion rotation)
        {
            GameObject prefab = GetPrefabForPropType(category);
            GameObject obj = SpawnFromPool(prefab, position, rotation);
            if (obj != null)
            {
                StatSpawnPointData propStats = CalculatePropStats(category);
                RegisterStatSpawnPointData(obj, propStats);
            }
            return obj;
        }

        public void RecycleToPool(GameObject instance)
        {
            if (instance == null) return;

            if (instance.TryGetComponent<ILootableEntity>(out var lootable))
            {
                lootable.DropLootOnDeactivate();
            }
            else if (_activeStatsRegistry.TryGetValue(instance, out StatSpawnPointData stats))
            {
                int dropChance = stats.lootDropChance.use ? stats.lootDropChance.value : 0;
                int lootQty = stats.lootQuantity.use ? stats.lootQuantity.value : 0;
                if (lootQty > 0 && UnityEngine.Random.Range(0, 100) < dropChance)
                {
                    for (int l = 0; l < lootQty; l++)
                    {
                        SpawnItem(stats.lootItemType, instance.transform.position, instance.transform.rotation);
                    }
                    OnLootDropped?.Invoke(instance, stats.lootItemType, lootQty);
                }
            }

            if (instance.TryGetComponent<IPoolRecyclable>(out var recyclable))
            {
                recyclable.RecycleEntity();
            }

            OnEntityRecycled?.Invoke(instance);

            int instanceID = instance.GetInstanceID();

            if (_activeStatsRegistry.ContainsKey(instance))
            {
                _activeStatsRegistry.Remove(instance);
            }

            bool isNetworkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            bool isNetworkObj = instance.TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned;

            if (isNetworkActive && isNetworkObj && NetworkManager.Singleton.IsServer)
            {
                netObj.Despawn(false);
            }

            if (_activeInstanceToKey.TryGetValue(instanceID, out string poolKey))
            {
                instance.SetActive(false);
                if (!instance.TryGetComponent<NetworkObject>(out _))
                {
                    instance.transform.SetParent(_poolParentTransform);
                }
                _objectPools[poolKey].Enqueue(instance);
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

        private GameObject TryFindExistingSceneEntity(Transform pointTransform, string tagFilter, System.Type componentType, HashSet<GameObject> assignedSet)
        {
            if (pointTransform == null) return null;

            GameObject ptGo = pointTransform.gameObject;
            if (ptGo != null && ptGo.activeInHierarchy && !assignedSet.Contains(ptGo))
            {
                if (IsMatchingEntity(ptGo, tagFilter, componentType))
                {
                    assignedSet.Add(ptGo);
                    return ptGo;
                }
            }

            foreach (Transform child in pointTransform)
            {
                GameObject childGo = child.gameObject;
                if (childGo != null && childGo.activeInHierarchy && !assignedSet.Contains(childGo))
                {
                    if (IsMatchingEntity(childGo, tagFilter, componentType))
                    {
                        assignedSet.Add(childGo);
                        return childGo;
                    }
                }
            }

            Collider[] colliders = Physics.OverlapSphere(pointTransform.position, 2.5f);
            for (int i = 0; i < colliders.Length; i++)
            {
                GameObject colGo = colliders[i].gameObject;
                if (colGo == null || !colGo.activeInHierarchy || assignedSet.Contains(colGo)) continue;
                if (_poolParentTransform != null && colGo.transform.IsChildOf(_poolParentTransform)) continue;

                if (IsMatchingEntity(colGo, tagFilter, componentType))
                {
                    assignedSet.Add(colGo);
                    return colGo;
                }
            }

            return null;
        }

        private bool IsMatchingEntity(GameObject go, string tagFilter, System.Type componentType)
        {
            if (go == null) return false;
            if (!string.IsNullOrEmpty(tagFilter) && go.CompareTag(tagFilter)) return true;
            if (componentType != null && go.GetComponent(componentType) != null) return true;
            return false;
        }

        public void ScanAndRegisterExistingSceneEntities(HashSet<GameObject> assignedSet = null)
        {
            HashSet<GameObject> assigned = assignedSet ?? _assignedSceneObjects;

            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            for (int i = 0; i < players.Length; i++)
            {
                GameObject p = players[i];
                if (p != null && p.activeInHierarchy && !_activeStatsRegistry.ContainsKey(p) && !assigned.Contains(p))
                {
                    if (_poolParentTransform != null && p.transform.IsChildOf(_poolParentTransform)) continue;
                    RegisterNetworkPlayer(p);
                    assigned.Add(p);
                }
            }

            GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
            for (int i = 0; i < enemies.Length; i++)
            {
                GameObject e = enemies[i];
                if (e != null && e.activeInHierarchy && !_activeStatsRegistry.ContainsKey(e) && !assigned.Contains(e))
                {
                    if (_poolParentTransform != null && e.transform.IsChildOf(_poolParentTransform)) continue;
                    HostType hostType = HostType.Melee;
                    if (e.TryGetComponent<EnemyController>(out var enemyCtrl))
                    {
                        hostType = enemyCtrl.activeArchetype switch
                        {
                            AIArchetype.FlanqueoYCobertura => HostType.Range,
                            AIArchetype.AtaqueYHuida => HostType.Range,
                            AIArchetype.InvocadorRefuerzos => HostType.Hybrid,
                            _ => HostType.Melee
                        };
                    }
                    StatSpawnPointData enemyStats = CalculateHostStats(hostType, i, Mathf.Max(1, enemies.Length));
                    RegisterStatSpawnPointData(e, enemyStats);
                    assigned.Add(e);
                }
            }
        }

        public void InitializeSceneSpawns()
        {
            _assignedSceneObjects.Clear();

            int totalPlayers = userSpawnPoints.Count;
            for (int i = 0; i < totalPlayers; i++)
            {
                UserSpawnPointData playerData = userSpawnPoints[i];
                GameObject playerPrefabToUse = playerData.customPrefab != null ? playerData.customPrefab : userPlayerPrefab;

                GameObject existingPlayer = TryFindExistingSceneEntity(playerData.pointTransform, "Player", typeof(PlayerController), _assignedSceneObjects);
                GameObject player = existingPlayer;

                if (player == null && playerPrefabToUse != null && playerData.pointTransform != null)
                {
                    player = SpawnFromPool(playerPrefabToUse, playerData.pointTransform.position, playerData.pointTransform.rotation);
                }

                if (player != null)
                {
                    if (existingPlayer != null && playerData.pointTransform != null)
                    {
                        player.transform.SetPositionAndRotation(CalibrateSpawnPosition(playerData.pointTransform.position, player), playerData.pointTransform.rotation);
                    }
                    StatSpawnPointData playerStats = CalculateUserStats(playerData.playerTypeToSpawn);
                    RegisterStatSpawnPointData(player, playerStats);
                }
            }

            int totalEnemies = hostSpawnPoints.Count;
            for (int i = 0; i < totalEnemies; i++)
            {
                HostSpawnPointData spawnData = hostSpawnPoints[i];
                GameObject enemyPrefabToUse = spawnData.customPrefab != null ? spawnData.customPrefab : GetPrefabForEnemyType(spawnData.enemyTypeToSpawn);

                GameObject existingEnemy = TryFindExistingSceneEntity(spawnData.pointTransform, "Enemy", typeof(EnemyController), _assignedSceneObjects);
                GameObject enemy = existingEnemy;

                if (enemy == null && enemyPrefabToUse != null && spawnData.pointTransform != null)
                {
                    enemy = SpawnFromPool(enemyPrefabToUse, spawnData.pointTransform.position, spawnData.pointTransform.rotation);
                }

                if (enemy != null)
                {
                    if (existingEnemy != null && spawnData.pointTransform != null)
                    {
                        enemy.transform.SetPositionAndRotation(CalibrateSpawnPosition(spawnData.pointTransform.position, enemy), spawnData.pointTransform.rotation);
                    }
                    StatSpawnPointData enemyStats = CalculateHostStats(spawnData.enemyTypeToSpawn, i, totalEnemies);
                    RegisterStatSpawnPointData(enemy, enemyStats);
                }
            }

            int totalItems = itemSpawnPoints.Count;
            for (int i = 0; i < totalItems; i++)
            {
                ItemSpawnPointData ItemsData = itemSpawnPoints[i];
                if (ItemsData.pointTransform != null)
                {
                    GameObject prefabToUse = ItemsData.customPrefab != null ? ItemsData.customPrefab : GetPrefabForItemType(ItemsData.categoryToSpawn);
                    GameObject existingItem = TryFindExistingSceneEntity(ItemsData.pointTransform, null, typeof(ILootableEntity), _assignedSceneObjects);
                    GameObject itemObj = existingItem;

                    if (itemObj == null && prefabToUse != null)
                    {
                        itemObj = SpawnFromPool(prefabToUse, ItemsData.pointTransform.position, ItemsData.pointTransform.rotation);
                    }

                    if (itemObj != null)
                    {
                        if (existingItem != null)
                        {
                            itemObj.transform.SetPositionAndRotation(CalibrateSpawnPosition(ItemsData.pointTransform.position, itemObj), ItemsData.pointTransform.rotation);
                        }
                        itemObj.transform.localScale = Vector3.one * DEFAULT_ITEM_SCALE;
                        StatSpawnPointData itemStats = CalculateItemStats(ItemsData.categoryToSpawn);
                        RegisterStatSpawnPointData(itemObj, itemStats);
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
                    GameObject existingProp = TryFindExistingSceneEntity(propData.pointTransform, null, null, _assignedSceneObjects);
                    GameObject propObj = existingProp;

                    if (propObj == null && prefabToUse != null)
                    {
                        propObj = SpawnFromPool(prefabToUse, propData.pointTransform.position, propData.pointTransform.rotation);
                    }

                    if (propObj != null)
                    {
                        if (existingProp != null)
                        {
                            propObj.transform.SetPositionAndRotation(CalibrateSpawnPosition(propData.pointTransform.position, propObj), propData.pointTransform.rotation);
                        }
                        StatSpawnPointData propStats = CalculatePropStats(propData.categoryToSpawn);
                        RegisterStatSpawnPointData(propObj, propStats);
                    }
                }
            }

            ScanAndRegisterExistingSceneEntities(_assignedSceneObjects);
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

            int difficultyFactor = 1 + DIFFICULTY_LEVEL;
            int baseThreat = SafeMultiply(threat, difficultyFactor);
            return Math.Max(ENEMY_MIN_TACTICAL_HEALTH, SafeMultiply(baseThreat, AGGRO_MULTIPLIER));
        }

        private const int MAX_PROP_HEALTH = 10000;
        private const int MAX_ENTITY_HEALTH = 1000000;

        private int SafeMultiply(int a, int b)
        {
            long result = (long)a * b;
            if (result > MAX_ENTITY_HEALTH) return MAX_ENTITY_HEALTH;
            if (result < -MAX_ENTITY_HEALTH) return -MAX_ENTITY_HEALTH;
            return (int)result;
        }

        public int CalculateItemValue(ItemType category)
        {
            int totalThreat = GetTotalEnemyThreatWeight();
            int itemCount = Math.Max(1, itemSpawnPoints.Count);

            int scaledVal = SafeMultiply(totalThreat, 100) / itemCount;
            int baseItemVal = SafeMultiply(scaledVal / 100, ENEMY_MIN_TACTICAL_HEALTH);

            int categoryFactor = 1 + (int)category;
            return Math.Max(1, SafeMultiply(baseItemVal, categoryFactor));
        }

        public StatSpawnPointData CalculateHostStats(HostType enemyType, int spawnIndex, int totalSpawns)
        {
            int typeVal = (int)enemyType;
            int typeFactor = 1 + typeVal;

            int minHits = Math.Max(1, TARGET_MIN_HITS_TO_KILL); // 2
            int maxBossHits = Math.Max(minHits, TARGET_MAX_BOSS_HITS); // 20

            // Criterio Fundamentado: Sincronización exacta de Hits-to-Kill (HTK)
            // Asumiendo un daño base del jugador de ~20:
            // - Enemigos grunts (Melee/Range) caen en 2 a 3 golpes (40 - 60 HP).
            // - Enemigos Híbridos/Bosses caen en maxBossHits golpes (400 HP).
            int estimatedPlayerDamage = 20;
            int targetHits = typeVal >= 2 ? maxBossHits : minHits;
            int health = estimatedPlayerDamage * targetHits;

            // Criterio Fundamentado: Daño del enemigo calibrado para que el jugador sea derrotado en múltiples intercambios
            int baseEnemyDmg = 15;
            int damage = Math.Max(MIN_BASE_DAMAGE, baseEnemyDmg * typeFactor); // Melee = 30, Range = 45, etc.

            int speed = Math.Max(MIN_BASE_SPEED, MIN_BASE_SPEED + Math.Max(0, 4 - typeVal));
            int defense = Math.Min(80, Math.Max(MIN_BASE_DEFENSE, MIN_BASE_DEFENSE + (typeVal * 5) + (DIFFICULTY_LEVEL * 2)));

            int detRadius = Math.Max(1, BASE_DETECTION_RADIUS + (typeVal * 5) + (DIFFICULTY_LEVEL * 2));
            int sRange = enemyType == HostType.Range || enemyType == HostType.Hybrid ? Math.Max(1, BASE_SHOOT_RANGE + (typeVal * 3)) : 0;
            int mRange = enemyType == HostType.Melee || enemyType == HostType.Hybrid ? Math.Max(1, BASE_MELEE_RANGE + typeVal) : 0;
            int wRadius = Math.Max(1, BASE_WANDER_RADIUS + (typeVal * 2));

            int ammo = Math.Max(1, BASE_MAX_AMMO + (typeVal * 10));
            int shield = typeVal >= 2 ? Math.Max(1, BASE_SHIELD_CAPACITY + (typeVal * 15)) : 0;
            int fuel = Math.Max(1, BASE_FUEL_CAPACITY + (typeVal * 20));

            int diffFactor = 1 + DIFFICULTY_LEVEL;
            int baseExp = 10 + SafeMultiply(typeVal, 15);
            int expStep = SafeMultiply(baseExp, diffFactor);
            int exp = Math.Max(1, SafeMultiply(expStep, AGGRO_MULTIPLIER));

            int nextExp = Math.Max(1, BASE_EXP_TO_LEVEL + (DIFFICULTY_LEVEL * 50));
            int atkPerLvl = Math.Max(1, 1 + typeVal);
            int defPerLvl = Math.Max(1, 1 + typeVal);

            ItemType lootType = (ItemType)(Math.Abs(spawnIndex + typeVal) % 4);
            int lootQty = Math.Max(1, 1 + typeVal);
            int dropChance = Math.Min(100, 50 + (typeVal * 15));

            // Variables de Combate, Movimiento Aéreo, Escudos y Ataques Especiales (Enteros)
            int cd = Math.Max(100, 2000 - (typeVal * 300) - (DIFFICULTY_LEVEL * 50));
            int physRes = Math.Min(90, (typeVal * 10) + (DIFFICULTY_LEVEL * 2));
            int nrgRes = Math.Min(90, (typeVal * 8) + (DIFFICULTY_LEVEL * 3));
            int expRes = Math.Min(90, (typeVal * 5) + (DIFFICULTY_LEVEL * 4));
            int rRadius = Math.Max(1, BASE_WANDER_RADIUS + (typeVal * 3));
            int rThreshold = Math.Min(100, 15 + (typeVal * 5));
            int skillChance = Math.Min(100, 10 + (typeVal * 15) + (DIFFICULTY_LEVEL * 2));

            int jHeight = 2;
            int mJumps = typeVal >= 2 ? 2 : 1;
            int jetpackFlag = typeVal == 3 ? 1 : 0;
            int jpConsumption = SafeMultiply(5, typeFactor);
            int jpRecharge = SafeMultiply(10, diffFactor);

            int sDamage = SafeMultiply(damage, 2);
            int sRadius = 5;
            int fireRate = Math.Max(50, 1000 - SafeMultiply(typeVal, 100) - SafeMultiply(DIFFICULTY_LEVEL, 10));
            int spread = Math.Max(0, 15 - (typeVal * 3));

            int sRegen = SafeMultiply(2, typeFactor);
            int sDelay = Math.Max(1, 5 - typeVal);
            int sAngle = 90;

            int burstCapacity = (enemyType == HostType.Range || enemyType == HostType.Hybrid) ? Math.Max(3, 5 + (typeVal * 2)) : 1;
            int burstCd = Math.Max(300, 1500 - (typeVal * 200) + (DIFFICULTY_LEVEL * 100));
            int recalibrateDelay = Math.Max(400, 800 + (DIFFICULTY_LEVEL * 100));
            int maxFireDuration = Math.Max(2, 4 + typeVal);

            return new StatSpawnPointData
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
                lootDropChance = dropChance,
                attackCooldown = cd,
                physicalResistance = physRes,
                energyResistance = nrgRes,
                explosiveResistance = expRes,
                retreatRadius = rRadius,
                retreatHealthThreshold = rThreshold,
                specialSkillChance = skillChance,
                jumpHeight = jHeight,
                maxJumps = mJumps,
                hasJetpack = jetpackFlag,
                jetpackConsumptionRate = jpConsumption,
                jetpackRechargeRate = jpRecharge,
                smashDamage = sDamage,
                smashRadius = sRadius,
                shootFireRate = fireRate,
                shootSpread = spread,
                shieldRegenRate = sRegen,
                shieldRechargeDelay = sDelay,
                shieldBlockAngle = sAngle,
                burstShotsCapacity = burstCapacity,
                burstCooldownDuration = burstCd,
                targetRecalibrationDelay = recalibrateDelay,
                maxContinuousFireDuration = maxFireDuration
            };
        }

        public StatSpawnPointData CalculateItemStats(ItemType category)
        {
            int baseVal = CalculateItemValue(category);
            int mag = baseVal;
            int rarity = Math.Max(1, 100 - ((int)category * 20) - (DIFFICULTY_LEVEL * 5));

            int hRest = category == ItemType.Consumible ? SafeMultiply(mag, 2) : 0;
            int fRest = category == ItemType.Resource ? SafeMultiply(mag, 3) : 0;
            int sRest = category == ItemType.Consumible ? SafeMultiply(mag, 2) : 0;
            int aRest = category == ItemType.Thing ? SafeMultiply(mag, 5) : 0;

            return new StatSpawnPointData
            {
                maxHealth = 1,
                currentHealth = 1,
                effectMagnitude = mag,
                itemRarity = rarity,
                lootItemType = category,
                lootQuantity = 1,
                lootDropChance = 100,
                healthRestoreAmount = hRest,
                fuelRestoreAmount = fRest,
                shieldRestoreAmount = sRest,
                ammoRestoreAmount = aRest
            };
        }

        public StatSpawnPointData CalculateUserStats(UserType playerType)
        {
            int typeVal = (int)playerType;
            int userFactor = 1 + typeVal;

            // Criterio Fundamentado: Salud del jugador con progresión lineal controlada por nivel de dificultad
            int basePlayerHp = PLAYER_MIN_SURVIVAL_HEALTH; // 250
            int health = basePlayerHp + (DIFFICULTY_LEVEL * 25);

            int diffFactor = 1 + DIFFICULTY_LEVEL;
            int dmgStep1 = SafeMultiply(MIN_BASE_DAMAGE, userFactor);
            int dmgStep2 = SafeMultiply(dmgStep1, diffFactor);
            int damage = Math.Max(MIN_BASE_DAMAGE, SafeMultiply(dmgStep2, AGGRO_MULTIPLIER));

            int speed = Math.Max(MIN_BASE_SPEED, MIN_BASE_SPEED + 6);
            int defense = Math.Min(85, Math.Max(MIN_BASE_DEFENSE, MIN_BASE_DEFENSE + (typeVal * 5) + (DIFFICULTY_LEVEL * 2)));

            int detRadius = Math.Max(1, BASE_DETECTION_RADIUS * 2);
            int sRange = Math.Max(1, BASE_SHOOT_RANGE * 2);
            int mRange = Math.Max(1, BASE_MELEE_RANGE + userFactor);
            int wRadius = 0;

            int ammo = Math.Max(1, BASE_MAX_AMMO * userFactor);
            int shield = Math.Max(1, BASE_SHIELD_CAPACITY * userFactor);
            int fuel = Math.Max(1, BASE_FUEL_CAPACITY * userFactor);

            int nextExp = Math.Max(1, BASE_EXP_TO_LEVEL + (DIFFICULTY_LEVEL * 50));
            int atkPerLvl = Math.Max(1, 2 + typeVal);
            int defPerLvl = Math.Max(1, 2 + typeVal);

            // Estadísticas avanzadas del Jugador (User) con Jetpack y Doble Salto habilitados por defecto
            int uBurstCapacity = 30;
            int uBurstCd = 1200;
            int uRecalibrateDelay = 200;
            int uMaxFireDuration = 10;

            return new StatSpawnPointData
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
                lootDropChance = 0,
                jumpHeight = 3,
                maxJumps = 2,
                burstShotsCapacity = uBurstCapacity,
                burstCooldownDuration = uBurstCd,
                targetRecalibrationDelay = uRecalibrateDelay,
                maxContinuousFireDuration = uMaxFireDuration,
                hasJetpack = 1,
                jetpackConsumptionRate = 4,
                jetpackRechargeRate = 12,
                smashDamage = SafeMultiply(damage, 3),
                smashRadius = 6,
                shootFireRate = 200,
                shootSpread = 2,
                shieldRegenRate = 5,
                shieldRechargeDelay = 2,
                shieldBlockAngle = 180
            };
        }

        public StatSpawnPointData CalculatePropStats(PropType category)
        {
            int propVal = (int)category;
            int propFactor = 1 + propVal;
            int sceneThreat = GetTotalEnemyThreatWeight();

            int step1 = SafeMultiply(PROP_MIN_DURABILITY, propFactor);
            int step2 = SafeMultiply(step1, DEFAULT_PROP_HEALTH_MULT);
            int rawHealth = SafeMultiply(step2, Math.Max(1, sceneThreat));
            int health = Math.Max(PROP_MIN_DURABILITY, Math.Min(MAX_PROP_HEALTH, rawHealth));

            ItemType propLootType = category == PropType.Tree ? ItemType.Resource : ItemType.Thing;
            int destDmg = category == PropType.Building ? SafeMultiply(50, DIFFICULTY_LEVEL) : 0;
            int destRad = category == PropType.Building ? 10 : 0;
            int reqDmgType = category == PropType.Building ? 2 : 0;

            return new StatSpawnPointData
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
                expReward = SafeMultiply(propFactor, 5),
                expToNextLevel = 0,
                attackPerLevel = 0,
                defensePerLevel = 0,
                lootItemType = propLootType,
                lootQuantity = Math.Max(1, propFactor),
                lootDropChance = 100,
                destructionDamage = destDmg,
                destructionRadius = destRad,
                requiredDamageType = reqDmgType
            };
        }

        private void RegisterStatSpawnPointData(GameObject entity, StatSpawnPointData stats)
        {
            _activeStatsRegistry[entity] = stats;
            if (entity.TryGetComponent<IBalanceCalibratable>(out var calibratable))
            {
                calibratable.CalibrateStats(stats);
            }
            OnEntityCalibrated?.Invoke(entity, stats);
        }

        public StatSpawnPointData? GetStatSpawnPointData(GameObject entity)
        {
            if (_activeStatsRegistry.TryGetValue(entity, out StatSpawnPointData stats))
            {
                return stats;
            }
            return null;
        }

        public StatSpawnPointData GetEntityBalance(GameObject entity)
        {
            return GetStatSpawnPointData(entity) ?? default;
        }

        #endregion
    }
}