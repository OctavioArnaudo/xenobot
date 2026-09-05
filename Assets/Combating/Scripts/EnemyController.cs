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
            if (renderRoot == null) renderRoot = transform.Find("Render") ?? transform.Find("PlayerRender");

            RefreshBodyReferences();
            EnsureEnemyModules();
        }

        private void EnsureEnemyModules()
        {
            if (enemyModules == null || enemyModules.Count == 0) return;

            foreach (var prefab in enemyModules)
            {
                if (prefab == null) continue;

                // Simple check to avoid double instantiation
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
            if (enemyModules == null) return null;
            return enemyModules.FirstOrDefault(x => x != null && x.name == prefabName);
        }
    }
}
