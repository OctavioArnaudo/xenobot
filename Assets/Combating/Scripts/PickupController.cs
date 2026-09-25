using UnityEngine;
using Unity.Netcode;
using Combating.Scripts;
using TMPro;

namespace Crafting.Scripts
{
    /// <summary>
    /// Universal Pickup Controller for Xenobot.
    /// Handles all items generically using ItemType and prefab logic.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PickupController : NetworkBehaviour
    {
        [Header("Data Configuration")]
        public ItemData item;

        public static int ActiveCount { get; private set; }

        [Header("Motion")]
        public float rotationSpeed = 100f;
        public float bobbingAmount = 0.15f;
        public float bobbingSpeed = 2f;

        private Vector3 _startPos;
        private float _timer;
        private bool _taken;
        private float _spawnTime;
        private bool _grounded = false;
        private string _lastDisplayedText;
        private const float PICKUP_DELAY = 0.2f;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        void Awake()
        {
            _spawnTime = Time.time;
            CreateMasterTrigger();
        }

        private void CreateMasterTrigger()
        {
            // 1. Force a trigger for player detection
            SphereCollider sc = GetComponent<SphereCollider>();
            if (sc == null) sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 1.2f;

            // 2. Ensure a solid collider for ground collision
            BoxCollider bc = GetComponent<BoxCollider>();
            if (bc == null)
            {
                bc = gameObject.AddComponent<BoxCollider>();
                bc.size = new Vector3(0.5f, 0.5f, 0.5f);
                bc.isTrigger = false;
            }

            // 3. Ensure a Rigidbody for trigger detection
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        void Start()
        {
            _startPos = transform.position;
            _spawnTime = Time.time;
            ActiveCount++;
            InventoryController.MarkCountDirty();

            UpdateFloatingName();
        }

        void Update()
        {
            _timer += Time.deltaTime;
            transform.localRotation = Quaternion.Euler(0, _timer * rotationSpeed, 0);

            string currentName = GetItemDisplayName();
            if (_lastDisplayedText != currentName)
            {
                UpdateFloatingName();
            }

            if (TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic)
            {
                _startPos = transform.position;
            }
            else
            {
                float bobbing = Mathf.Sin(Time.time * bobbingSpeed) * bobbingAmount;
                transform.position = new Vector3(_startPos.x, _startPos.y + bobbing, _startPos.z);
            }
        }

        private void UpdateFloatingName()
        {
            string displayName = GetItemDisplayName();
            if (string.IsNullOrEmpty(displayName)) return;

            Transform existing = transform.Find("LootMsg");
            GameObject msgGo;
            TextMeshPro tmp;

            if (existing != null)
            {
                msgGo = existing.gameObject;
                tmp = msgGo.GetComponent<TextMeshPro>();
            }
            else
            {
                msgGo = new GameObject("LootMsg");
                msgGo.transform.SetParent(transform);
                msgGo.transform.localPosition = Vector3.up * 1.0f;
                tmp = msgGo.AddComponent<TextMeshPro>();
                tmp.fontSize = 3;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.yellow;
                msgGo.AddComponent<SimpleBillboard>();
            }

            if (tmp != null)
            {
                tmp.text = displayName;
            }

            _lastDisplayedText = displayName;
        }

        private string GetItemDisplayName()
        {
            if (item != null && !string.IsNullOrEmpty(item.displayName))
            {
                return item.displayName;
            }

            // Fallback: usar el nombre limpio del gameObject/prefab
            string cleanName = gameObject.name;
            int cloneIndex = cleanName.IndexOf("(Clone)", System.StringComparison.OrdinalIgnoreCase);
            if (cloneIndex > 0)
            {
                cleanName = cleanName.Substring(0, cloneIndex);
            }
            cleanName = cleanName.Replace("Prefab", "").Replace("Item_", "").Replace("Item", "").Trim();
            if (string.IsNullOrEmpty(cleanName)) cleanName = gameObject.name;
            return cleanName;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!_grounded && TryGetComponent<Rigidbody>(out var rb))
            {
                if (!rb.isKinematic)
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    _grounded = true;
                    _startPos = transform.position + Vector3.up * 0.4f;
                }
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (_taken || Time.time < _spawnTime + PICKUP_DELAY) return;

            // Robust player detection: Check root and parent hierarchy
            Transform root = other.transform.root;
            InventoryController inv = root.GetComponentInChildren<InventoryController>();
            bool isPlayer = root.CompareTag("Player") || other.CompareTag("Player") || inv != null;

            if (isPlayer)
            {
                ResolveItemDataIfNeeded();

                _taken = true;
                if (IsNetworkActive) ProcessPickupAuthoritative(inv, root.gameObject);
                else ProcessPickupLocal(inv, root.gameObject);
            }
        }

        private void ResolveItemDataIfNeeded()
        {
            if (item != null) return;

#if UNITY_EDITOR
            string cleanName = GetItemDisplayName().Replace(" ", "");
            string[] possiblePaths = new string[]
            {
                $"Assets/Crafting/Data/Item_{cleanName}.asset",
                $"Assets/Crafting/Data/Item_{gameObject.name.Replace("Prefab", "").Replace("(Clone)", "").Trim()}.asset"
            };

            foreach (var path in possiblePaths)
            {
                item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (item != null) break;
            }
#endif
        }

        private void ProcessPickupAuthoritative(InventoryController inv, GameObject player)
        {
            if (IsServer)
            {
                ApplyReward(inv, player);
                SpawnPickupEffectClientRpc();

                NetworkObject netObj = GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                {
                    if (netObj.InScenePlaced) { netObj.Despawn(false); Destroy(gameObject); }
                    else netObj.Despawn(true);
                }
            }
        }

        private void ProcessPickupLocal(InventoryController inv, GameObject player)
        {
            ApplyReward(inv, player);
            SpawnHardcodedEffect();
            Destroy(gameObject);
        }

        private void ApplyReward(InventoryController inv, GameObject player)
        {
            // 1. Si el objeto tiene un SpawnController con loot o itemsToSpawn, activamos el spawneo al interactuar/recoger la primera vez
            var sc = GetComponent<SpawnController>();
            if (sc != null)
            {
                sc.TriggerDeath();
                if (this == null) return;
            }

            ResolveItemDataIfNeeded();

            // Ejecutar acciones de pickup o efectos funcionales directamente del objeto
            var pickupActions = GetComponentsInChildren<IItemPickupAction>(true);
            if (pickupActions.Length > 0)
            {
                foreach (var act in pickupActions) act.OnPickupItem(player);
            }
            else
            {
                foreach (var func in GetComponentsInChildren<IItemFunctional>(true))
                {
                    if (func is SpawnController || func is PickupController) continue;
                    func.ApplyEffect(player);
                }
            }

            // Si se resolvió un ItemData y no es autoUse ni experiencia, agregarlo al inventario
            if (item != null && inv != null && !item.autoUse && item.type != ItemType.Experience)
            {
                int hash = item.GetItemHashCode();
                if (IsNetworkActive) inv.AddItemServerRpc(hash, 1);
                else inv.InternalAddItem(hash, 1);
            }
        }

        [ClientRpc]
        private void SpawnPickupEffectClientRpc() => SpawnHardcodedEffect();

        private void SpawnHardcodedEffect()
        {
            GameObject go = new GameObject("PickupEffect");
            go.transform.position = transform.position;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = 0.5f;
            main.startSpeed = 5f;
            main.startSize = 0.2f;
            main.startColor = (item != null && item.type == ItemType.Experience) ? Color.yellow : Color.cyan;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0, 20) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null) renderer.material = new Material(shader);

            ps.Play();
            Destroy(go, 2.0f);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            ActiveCount--;
            InventoryController.MarkCountDirty();
        }
    }
}
