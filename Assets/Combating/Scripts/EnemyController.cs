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

        private void Awake()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                InitializeComponents();
            }
        }

        public override void OnNetworkSpawn()
        {
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
                if (moduleType != null && GetComponentInChildren(moduleType, true) != null) continue;

                GameObject instance = Instantiate(prefab, transform);
                instance.name = prefab.name;

                foreach (var module in instance.GetComponentsInChildren<IModular>(true))
                {
                    module.Bind(this);
                }

                if (IsServer && IsSpawned && instance.TryGetComponent<NetworkObject>(out var netObj))
                {
                    netObj.Spawn(true);
                }
            }
        }

        private System.Type GetModuleTypeFromPrefab(GameObject prefab)
        {
            var modular = prefab.GetComponentInChildren<IModular>(true);
            return modular?.GetType();
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
