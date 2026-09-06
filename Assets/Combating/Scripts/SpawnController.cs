using UnityEngine;
using Unity.Netcode;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using Crafting.Scripts;

namespace Combating.Scripts
{
    [System.Serializable]
    public struct SpawnableItem
    {
        public GameObject prefab;
        public string message;
    }

    public class SpawnController : NetworkBehaviour, IModular
    {
        // Spawning Reliability Constants
        private const float DropForwardOffset = 1.2f;
        private const float DropUpOffset = 0.5f;
        private const float DropImpulseForce = 3.0f;

        [Header("Spawn Settings")]
        public List<ItemData> lootTable = new List<ItemData>();
        public List<SpawnableItem> itemsToSpawn = new List<SpawnableItem>();
        public float explosionForce = 10f;
        public float spreadRadius = 2.5f;

        private ModularController _hub;

        private void Awake()
        {
            _hub = GetComponentInParent<ModularController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null) _hub.RegisterModule(this);
        }

        public void OnRefreshModule() { }

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        public void TriggerDeath()
        {
            if (IsNetworkActive && !IsServer) return;

            CreateDeathVisuals();
            SpawnItems();

            if (IsNetworkActive && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(false);
                Destroy(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void CreateDeathVisuals()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = transform.position;
            sphere.transform.localScale = Vector3.one * 0.5f;
            if (sphere.TryGetComponent<Collider>(out var c)) Destroy(c);

            var mr = sphere.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            mat.color = Color.white;
            mr.material = mat;
            Destroy(sphere, 0.15f);

            for (int i = 0; i < 8; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = transform.position + (Random.insideUnitSphere * 0.3f);
                cube.transform.localScale = Vector3.one * Random.Range(0.1f, 0.25f);
                var rb = cube.AddComponent<Rigidbody>();
                rb.AddExplosionForce(explosionForce, transform.position, spreadRadius);
                var r = cube.GetComponent<Renderer>();
                r.material.color = Color.Lerp(Color.red, Color.black, Random.value);
                Destroy(cube, 1.0f);
            }
        }

        public void SpawnDroppedItem(GameObject prefab, string message = "")
        {
            if (prefab == null) return;

            // Calculate centralized spawn position
            Vector3 spawnPos = transform.position + transform.forward * DropForwardOffset + transform.up * DropUpOffset;
            Vector3 impulse = (transform.forward + transform.up).normalized * DropImpulseForce;

            SpawnSingleItem(prefab, spawnPos, message, impulse);
        }

        public void SpawnSingleItem(GameObject prefab, Vector3 position, string message = "", Vector3? impulse = null)
        {
            if (prefab == null) return;
            GameObject spawned = Instantiate(prefab, position, Quaternion.identity);
            if (!string.IsNullOrEmpty(message))
            {
                GameObject msgGo = new GameObject("LootMsg");
                msgGo.transform.SetParent(spawned.transform);
                msgGo.transform.localPosition = Vector3.up * 1.0f;
                var tmp = msgGo.AddComponent<TextMeshPro>();
                tmp.text = message;
                tmp.fontSize = 3;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.yellow;
                msgGo.AddComponent<SimpleBillboard>();
            }
            Rigidbody rb = spawned.GetComponent<Rigidbody>();
            if (rb == null) rb = spawned.AddComponent<Rigidbody>();
            if (rb != null)
            {
                var meshColliders = spawned.GetComponentsInChildren<MeshCollider>();
                foreach (var mc in meshColliders) mc.convex = true;
                rb.isKinematic = false;
                rb.useGravity = true;
                Vector3 force = impulse ?? (transform.forward * 2f + Random.insideUnitSphere * 0.5f);
                rb.AddForce(force, ForceMode.Impulse);
            }
            if (IsNetworkActive && IsServer)
            {
                if (spawned.TryGetComponent<NetworkObject>(out var netObj)) netObj.Spawn();
            }
        }

        private void SpawnItems()
        {
            // 1. Priority: Manual Loot from Inspector
            foreach (var item in lootTable)
            {
                if (item != null) SpawnDroppedItem(item.itemPrefab, item.displayName);
            }

            foreach (var item in itemsToSpawn)
            {
                if (item.prefab != null) SpawnDroppedItem(item.prefab, item.message);
            }

            // 2. Dynamic Enemy Loot (Always EXP, Randomized Fuel/Life)
            if (_hub is EnemyController)
            {
                // Load items from Resources
                var allItems = Resources.LoadAll<ItemData>("");

                // Always EXP (search by type or common names)
                var expData = allItems.FirstOrDefault(x => x.type == ItemType.Experience || x.itemCode.ToLower() == "exp" || x.displayName.ToLower().Contains("exp"));
                if (expData != null) SpawnDroppedItem(expData.itemPrefab, expData.displayName);

                // Randomized Fuel (30%)
                if (Random.value <= 0.3f)
                {
                    var fuelData = allItems.FirstOrDefault(x => x.itemCode.ToLower() == "fuel" || x.displayName.ToLower().Contains("fuel"));
                    if (fuelData != null) SpawnDroppedItem(fuelData.itemPrefab, fuelData.displayName);
                }

                // Randomized Life (15%)
                if (Random.value <= 0.15f)
                {
                    var lifeData = allItems.FirstOrDefault(x => x.itemCode.ToLower() == "life" || x.displayName.ToLower().Contains("life"));
                    if (lifeData != null) SpawnDroppedItem(lifeData.itemPrefab, lifeData.displayName);
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
