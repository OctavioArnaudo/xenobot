using UnityEngine;
using Unity.Netcode;
using TMPro;
using System.Collections.Generic;
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

        public void SpawnDroppedItem(GameObject prefab, Vector3 origin, string message = "")
        {
            if (prefab == null) return;
            Vector3 offset = transform.right * 1.2f + transform.up * 0.5f;
            Vector3 spawnPos = origin + offset;
            Vector3 impulse = (transform.right + transform.up).normalized * 3f;
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
            foreach (var item in lootTable)
            {
                if (item != null) SpawnDroppedItem(item.itemPrefab, transform.position, item.displayName);
            }
            foreach (var item in itemsToSpawn)
            {
                SpawnDroppedItem(item.prefab, transform.position, item.message);
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
