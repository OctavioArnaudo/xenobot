using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace Combating.Scripts
{
    /// <summary>
    /// Manages spawning prefabs at child empty objects (SpawnPoints).
    /// Handles logic based on prefab type (e.g., spawn enemies only once).
    /// </summary>
    public class SpawnManager : NetworkBehaviour
    {
        [Header("Settings")]
        public GameObject prefabToSpawn;
        public bool spawnOnlyOnce = true;
        public float delayBetweenSpawns = 5f;

        private List<Transform> m_SpawnPoints = new List<Transform>();
        private bool m_HasSpawned = false;

        private void Awake()
        {
            // Auto-detect spawn points in children
            foreach (Transform child in transform)
            {
                if (child.childCount == 0) // Treat empty children as spawn points
                    m_SpawnPoints.Add(child);
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            TriggerSpawn();
        }

        private void Start()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                TriggerSpawn();
        }

        public void TriggerSpawn()
        {
            if (m_HasSpawned && spawnOnlyOnce) return;

            foreach (var point in m_SpawnPoints)
            {
                GameObject spawned = Instantiate(prefabToSpawn, point.position, point.rotation);

                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    if (spawned.TryGetComponent<NetworkObject>(out var netObj))
                        netObj.Spawn();
                }
            }

            m_HasSpawned = true;
        }
    }
}
