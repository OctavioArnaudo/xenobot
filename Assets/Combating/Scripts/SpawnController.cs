using UnityEngine;
using Unity.Netcode;
using TMPro;
using System.Collections.Generic;

namespace Combating.Scripts
{
    [System.Serializable]
    public struct SpawnableItem
    {
        public GameObject prefab;
        public string message;
    }

    /// <summary>
    /// Handles visual death effects and loot spawning.
    /// Triggered by HealthController upon death.
    /// </summary>
    public class SpawnController : NetworkBehaviour
    {
        [Header("Spawn Settings")]
        public List<SpawnableItem> itemsToSpawn = new List<SpawnableItem>();
        public float explosionForce = 10f;
        public float spreadRadius = 2.5f;

        [Header("Sibling Scripts (Auto-detected)")]
        [SerializeField] private ShootController shooter;
        [SerializeField] private EnemyController enemyAI;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        void Awake()
        {
            // Auto-detect sibling action controllers if not assigned
            if (shooter == null) shooter = GetComponent<ShootController>();
            if (enemyAI == null) enemyAI = GetComponent<EnemyController>();
        }

        /// <summary>
        /// Called by HealthController when HP reaches zero.
        /// </summary>
        public void TriggerDeath()
        {
            if (IsNetworkActive && !IsServer) return;

            Debug.Log($"[SpawnController] {gameObject.name} death triggered. Cleaning up and spawning loot...");

            // 1. Disable logic components
            if (enemyAI != null) enemyAI.enabled = false;
            if (shooter != null) shooter.enabled = false;

            // 2. Visual Effects
            CreateDeathVisuals();

            // 3. Spawn Items
            SpawnItems();

            // 4. Cleanup
            if (IsNetworkActive && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void CreateDeathVisuals()
        {
            // Flash sphere (hardcoded)
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = transform.position;
            sphere.transform.localScale = Vector3.one * 0.5f;
            var renderer = sphere.GetComponent<Renderer>();
            renderer.material.color = Color.white;
            Destroy(sphere, 0.15f);

            // Debris burst (hardcoded)
            for (int i = 0; i < 10; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = transform.position + (Random.insideUnitSphere * 0.2f);
                cube.transform.localScale = Vector3.one * Random.Range(0.1f, 0.25f);

                var rb = cube.AddComponent<Rigidbody>();
                rb.AddExplosionForce(explosionForce * 1.2f, transform.position, spreadRadius);

                var r = cube.GetComponent<Renderer>();
                r.material.color = Color.Lerp(Color.red, Color.black, Random.value);

                Destroy(cube, 1.2f);
            }
        }

        private void SpawnItems()
        {
            foreach (var item in itemsToSpawn)
            {
                if (item.prefab == null) continue;

                Vector3 offset = Random.onUnitSphere * spreadRadius;
                offset.y = Mathf.Abs(offset.y) + 0.5f;
                Vector3 spawnPos = transform.position + offset;

                GameObject spawned = Instantiate(item.prefab, spawnPos, Quaternion.identity);

                // Add text message floating above
                GameObject msgGo = new GameObject("LootMsg");
                msgGo.transform.SetParent(spawned.transform);
                msgGo.transform.localPosition = Vector3.up * 1.2f;

                var tmp = msgGo.AddComponent<TextMeshPro>();
                tmp.text = item.message;
                tmp.fontSize = 4;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.yellow;
                msgGo.AddComponent<SimpleBillboard>();

                if (spawned.TryGetComponent<Rigidbody>(out Rigidbody rb))
                {
                    rb.AddExplosionForce(explosionForce, transform.position, spreadRadius);
                }

                if (IsNetworkActive && IsServer)
                {
                    if (spawned.TryGetComponent<NetworkObject>(out NetworkObject netObj))
                    {
                        netObj.Spawn();
                    }
                }
            }
        }
    }

    public class SimpleBillboard : MonoBehaviour
    {
        private Transform _camTransform;
        void Start() { if (Camera.main != null) _camTransform = Camera.main.transform; }
        void LateUpdate() { if (_camTransform != null) transform.LookAt(transform.position + _camTransform.forward); }
    }
}
