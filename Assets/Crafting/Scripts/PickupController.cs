using UnityEngine;
using Unity.Netcode;
using Combating.Scripts;

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
            PlayerController.MarkCountDirty();
        }

        void Update()
        {
            _timer += Time.deltaTime;
            transform.localRotation = Quaternion.Euler(0, _timer * rotationSpeed, 0);

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
            PlayerController inv = root.GetComponentInChildren<PlayerController>();
            bool isPlayer = root.CompareTag("Player") || other.CompareTag("Player") || inv != null;

            if (isPlayer)
            {
                if (item == null)
                {
                    Debug.LogWarning($"[Pickup] {gameObject.name} no tiene ItemData asignado.");
                    return;
                }

                _taken = true;
                if (IsNetworkActive) ProcessPickupAuthoritative(inv, root.gameObject);
                else ProcessPickupLocal(inv, root.gameObject);
            }
        }

        private void ProcessPickupAuthoritative(PlayerController inv, GameObject player)
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

        private void ProcessPickupLocal(PlayerController inv, GameObject player)
        {
            ApplyReward(inv, player);
            SpawnHardcodedEffect();
            Destroy(gameObject);
        }

        private void ApplyReward(PlayerController inv, GameObject player)
        {
            if (item == null) return;

            if (item.autoUse)
            {
                foreach(var func in GetComponentsInChildren<IItemFunctional>())
                {
                    func.ApplyEffect(player);
                }
            }
            else
            {
                // Correctly use the instance found instead of static LocalInstance
                if (inv != null)
                {
                    var items = inv.GetModule<ItemsController>();
                    if (items != null) items.AddItem(item);
                    else PlayerController.Add(item); // Fallback
                }
                else
                {
                    PlayerController.Add(item);
                }
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
            PlayerController.MarkCountDirty();
        }
    }
}
