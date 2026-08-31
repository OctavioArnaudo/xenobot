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

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        /// <summary>
        /// Called by HealthController when HP reaches zero.
        /// </summary>
        public void TriggerDeath()
        {
            // Solo el servidor o modo offline procesan la muerte real y el loot
            if (IsNetworkActive && !IsServer) return;

            Debug.Log($"[SpawnController] {gameObject.name} death triggered.");

            // 1. Efectos Visuales
            CreateDeathVisuals();

            // 2. Spawneo de Items
            SpawnItems();

            // 3. Limpieza de red o local
            if (IsNetworkActive && NetworkObject.IsSpawned)
            {
                // Para objetos colocados en escena, Despawn(false) evita el warning y luego destruimos localmente
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
            // Flash de muerte (esfera blanca temporal)
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

            // Explosión de escombros (cubos)
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

        public void SpawnSingleItem(GameObject prefab, Vector3 position, string message = "", Vector3? impulse = null)
        {
            if (prefab == null) return;

            GameObject spawned = Instantiate(prefab, position, Quaternion.identity);

            // Mensaje flotante de loot
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

            // Física para el item spawneado
            Rigidbody rb = spawned.GetComponent<Rigidbody>();
            if (rb == null) rb = spawned.AddComponent<Rigidbody>();

            if (rb != null)
            {
                Vector3 force = impulse ?? (transform.forward * 2f + Random.insideUnitSphere * 0.5f);
                rb.AddForce(force, ForceMode.Impulse);
            }

            // Sincronización en red
            if (IsNetworkActive && IsServer)
            {
                if (spawned.TryGetComponent<NetworkObject>(out var netObj))
                {
                    netObj.Spawn();
                }
            }
        }

        private void SpawnItems()
        {
            foreach (var item in itemsToSpawn)
            {
                SpawnSingleItem(item.prefab, transform.position + (Random.onUnitSphere * spreadRadius), item.message);
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
