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
    /// Unified controller for enemy spawning and death logic.
    /// Manages health and coordinates with modular combat sibling scripts.
    /// </summary>
    public class SpawnController : NetworkBehaviour
    {
        public int maxHealth = 100;

        [Header("Spawn Settings")]
        public List<SpawnableItem> itemsToSpawn = new List<SpawnableItem>();
        public float explosionForce = 10f;
        public float spreadRadius = 2.5f;

        [Header("Sibling Scripts (Auto-detected)")]
        [SerializeField] private CombatTeamMember teamMember;
        [SerializeField] private ClickToShoot shooter;
        [SerializeField] private BasicAttackAI attackAI;
        [SerializeField] private CombatDamageReceiver damageReceiver;

        private NetworkVariable<int> currentHealth = new NetworkVariable<int>(100);
        private int m_OfflineHealth;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        void Awake()
        {
            m_OfflineHealth = maxHealth;

            // Auto-detect sibling scripts if not assigned
            if (teamMember == null) teamMember = GetComponent<CombatTeamMember>();
            if (shooter == null) shooter = GetComponent<ClickToShoot>();
            if (attackAI == null) attackAI = GetComponent<BasicAttackAI>();
            if (damageReceiver == null) damageReceiver = GetComponent<CombatDamageReceiver>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                currentHealth.Value = maxHealth;
            }
        }

        public void TakeDamage(int damage)
        {
            if (IsNetworkActive)
            {
                if (!IsServer) return;
                if (currentHealth.Value <= 0) return;

                currentHealth.Value -= damage;
                Debug.Log($"[SpawnController] {gameObject.name} received {damage} damage (Network). HP: {currentHealth.Value}");

                if (currentHealth.Value <= 0) Die();
            }
            else
            {
                if (m_OfflineHealth <= 0) return;

                m_OfflineHealth -= damage;
                Debug.Log($"[SpawnController] {gameObject.name} received {damage} damage (Offline). HP: {m_OfflineHealth}");

                if (m_OfflineHealth <= 0) Die();
            }
        }

        private void Die()
        {
            if (IsNetworkActive && !IsServer) return;

            Debug.Log($"[SpawnController] {gameObject.name} has died. Spawning loot...");

            // 1. Coordinate with sibling scripts (Disable AI and shooting on death)
            if (attackAI != null) attackAI.enabled = false;
            if (shooter != null) shooter.enabled = false;

            // 2. Visual Death Effects (Hardcoded)
            CreateDeathVisuals();

            // 3. Spawn Items with Messages
            SpawnItems();

            // 4. Final destruction
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
            // Flash sphere
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = transform.position;
            sphere.transform.localScale = Vector3.one * 0.5f;
            var renderer = sphere.GetComponent<Renderer>();
            renderer.material.color = Color.white;
            Destroy(sphere, 0.15f);

            // Debris burst
            for (int i = 0; i < 12; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = transform.position + (Random.insideUnitSphere * 0.2f);
                cube.transform.localScale = Vector3.one * Random.Range(0.1f, 0.3f);

                var rb = cube.AddComponent<Rigidbody>();
                rb.mass = 0.5f;
                rb.AddExplosionForce(explosionForce * 1.5f, transform.position, spreadRadius);

                var r = cube.GetComponent<Renderer>();
                r.material.color = Color.Lerp(Color.red, Color.black, Random.value);

                Destroy(cube, 1.5f);
            }
        }

        private void SpawnItems()
        {
            foreach (var item in itemsToSpawn)
            {
                if (item.prefab == null) continue;

                // Position slightly offset from enemy center
                Vector3 offset = Random.onUnitSphere * spreadRadius;
                offset.y = Mathf.Abs(offset.y) + 0.5f; // Ensure it's above ground
                Vector3 spawnPos = transform.position + offset;

                GameObject spawned = Instantiate(item.prefab, spawnPos, Quaternion.identity);

                // Add text message floating above
                GameObject msgGo = new GameObject("LootMessage");
                msgGo.transform.SetParent(spawned.transform);
                msgGo.transform.localPosition = Vector3.up * 1.2f;

                var tmp = msgGo.AddComponent<TextMeshPro>();
                tmp.text = item.message;
                tmp.fontSize = 5;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.yellow;
                tmp.fontStyle = FontStyles.Bold;

                // Add a simple Billboard behavior
                msgGo.AddComponent<SimpleBillboard>();

                // Explosion physics on the item
                if (spawned.TryGetComponent<Rigidbody>(out Rigidbody rb))
                {
                    rb.AddExplosionForce(explosionForce, transform.position, spreadRadius);
                }

                if (IsNetworkActive)
                {
                    if (spawned.TryGetComponent<NetworkObject>(out NetworkObject netObj))
                    {
                        netObj.Spawn();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Helper to keep text facing the camera.
    /// </summary>
    public class SimpleBillboard : MonoBehaviour
    {
        private Transform _camTransform;

        void Start()
        {
            if (Camera.main != null) _camTransform = Camera.main.transform;
        }

        void LateUpdate()
        {
            if (_camTransform != null)
            {
                transform.LookAt(transform.position + _camTransform.forward);
            }
        }
    }
}
