using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

namespace Combating.Scripts
{
    /// <summary>
    /// Hardware Hub for Enemy Entities.
    /// Manages modules and physical references for AI.
    /// </summary>
    public class EnemyController : ModularController
    {
        [Header("Enemy Library")]
        public NetworkPrefabsList networkPrefabs;
        public List<GameObject> enemyModules;

        protected override void Awake()
        {
            base.Awake();
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                InitializeComponents();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                Level.Value = 1;
                Exp.Value = 0;
                ExpToLevelUp.Value = 100f;
                Attack.Value = UnityEngine.Random.Range(5f, 10f);
                Defense.Value = UnityEngine.Random.Range(2f, 6f);

                int hpBase = UnityEngine.Random.Range(65, 96);
                if (EnemyCount > 12) hpBase = Mathf.RoundToInt(hpBase * 0.9f);
                maxHealth.Value = hpBase;
                currentHealth.Value = maxHealth.Value;
            }

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            if (controller == null) controller = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // Nos aseguramos de que el Hardware Hub tenga el NavMeshAgent si no existe
            if (GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
            {
                gameObject.AddComponent<UnityEngine.AI.NavMeshAgent>();
            }

            // Forced Cleaning: Enemies must not have Cameras or AudioListeners in their hierarchy
            foreach (var cam in GetComponentsInChildren<Camera>(true))
            {
                Debug.Log($"[EnemyController] Sanitizing camera found in {gameObject.name} hierarchy.");
                DestroyImmediate(cam);
            }
            foreach (var listener in GetComponentsInChildren<AudioListener>(true))
            {
                Debug.Log($"[EnemyController] Sanitizing AudioListener found in {gameObject.name} hierarchy.");
                DestroyImmediate(listener);
            }

            RefreshBodyReferences();
            EnsureEnemyModules();
        }

        private void EnsureEnemyModules()
        {
            List<GameObject> allPrefabs = new List<GameObject>();
            if (enemyModules != null) allPrefabs.AddRange(enemyModules);
            if (networkPrefabs != null)
            {
                foreach (var item in networkPrefabs.PrefabList)
                {
                    if (item.Prefab != null) allPrefabs.Add(item.Prefab);
                }
            }

            if (allPrefabs.Count == 0) return;

            foreach (var prefab in allPrefabs)
            {
                if (prefab == null) continue;

                var moduleType = GetModuleTypeFromPrefab(prefab);
                if (moduleType == null || !IsEnemyModuleAllowed(moduleType)) continue;
                if (GetComponentInChildren(moduleType, true) != null) continue;

                GameObject instance = Instantiate(prefab, transform);
                instance.name = prefab.name;

                // Apply Hardware Sanitization
                SanitizeModuleInstance(instance);

                foreach (var module in instance.GetComponentsInChildren<IModular>(true))
                {
                    if (module is MonoBehaviour mb && !mb.enabled) continue;
                    module.Bind(this);
                }

                if (IsServer && IsSpawned && instance.TryGetComponent<NetworkObject>(out var netObj))
                {
                    if (netObj.enabled) netObj.Spawn(true);
                }
            }
        }

        private System.Type GetModuleTypeFromPrefab(GameObject prefab)
        {
            var modular = prefab.GetComponentInChildren<IModular>(true);
            return modular?.GetType();
        }

        private bool IsEnemyModuleAllowed(System.Type moduleType)
        {
            return moduleType == typeof(AiController)
                || moduleType == typeof(ShootController)
                || moduleType == typeof(SpawnController)
                || moduleType == typeof(HealthController)
                || moduleType == typeof(DamageController)
                || moduleType == typeof(HealController)
                || moduleType == typeof(DeathController)
                || moduleType == typeof(MeleeController)
                || moduleType == typeof(AnimationController)
                || moduleType == typeof(TankController);
        }

        public override GameObject GetPrefabFromList(string prefabName)
        {
            if (enemyModules != null)
            {
                var p = enemyModules.FirstOrDefault(x => x != null && x.name == prefabName);
                if (p != null) return p;
            }
            if (networkPrefabs != null)
            {
                var p = networkPrefabs.PrefabList.FirstOrDefault(x => x.Prefab != null && x.Prefab.name == prefabName);
                if (p.Prefab != null) return p.Prefab;
            }
            return null;
        }
    }
}
