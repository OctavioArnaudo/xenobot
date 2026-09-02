using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Universal Pickup Controller for Xenobot.
/// Handles Inventory Items, Experience Orbs, and Network Synchronization.
/// Logic is on the Root, Visuals/Colliders can be anywhere in hierarchy.
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
    private const float PICKUP_DELAY = 0.3f;

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    void Awake()
    {
        _spawnTime = Time.time;
        CreateMasterTrigger();
    }

    private void CreateMasterTrigger()
    {
        // 1. Asegurar que haya un trigger para detectar al jugador
        Collider[] colliders = GetComponents<Collider>();
        bool hasTrigger = false;
        bool hasSolid = false;
        foreach (var c in colliders)
        {
            if (c.isTrigger) hasTrigger = true;
            else hasSolid = true;
        }

        if (!hasTrigger)
        {
            SphereCollider sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.7f;
        }

        // 2. Asegurar un colisionador sólido para que no atraviese el suelo al caer
        if (!hasSolid)
        {
            BoxCollider bc = gameObject.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.5f, 0.5f, 0.5f);
            bc.isTrigger = false;
        }
        else
        {
            // Si ya tiene colisionadores sólidos, asegurar que si son MeshColliders sean Convexos
            // para evitar errores con Rigidbodies dinámicos
            var meshColliders = GetComponentsInChildren<MeshCollider>();
            foreach (var mc in meshColliders) mc.convex = true;
        }

        // 3. Asegurar Rigidbody
        if (GetComponent<Rigidbody>() == null)
        {
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
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
    }

    void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

        if (TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic)
        {
            // Mientras esté cayendo (no sea kinematic), actualizamos el punto de inicio
            _startPos = transform.position;
        }
        else
        {
            // Cuando ya está quieto (kinematic), flota sobre su _startPos
            _timer += Time.deltaTime * bobbingSpeed;
            transform.position = _startPos + new Vector3(0, Mathf.Sin(_timer) * bobbingAmount, 0);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Al tocar el suelo, desactivamos físicas para que empiece a flotar en ese punto
        if (!_grounded && TryGetComponent<Rigidbody>(out var rb))
        {
            if (!rb.isKinematic)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                _grounded = true;
                // Ajustamos _startPos un poco hacia arriba para que la flotación no atraviese el suelo
                _startPos = transform.position + Vector3.up * 0.4f;
                Debug.Log($"[Pickup] {gameObject.name} tocó suelo y comenzó a flotar.");
            }
        }
    }

    void OnTriggerStay(Collider other)
    {
        OnTriggerEnter(other);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_taken || Time.time < _spawnTime + PICKUP_DELAY) return;

        if (IsNetworkActive && !IsServer) return;

        InventoryController inv = other.GetComponentInParent<InventoryController>();
        bool isPlayer = other.CompareTag("Player") || inv != null;

        if (isPlayer)
        {
            if (item == null) return;
            _taken = true;
            if (IsNetworkActive) ProcessPickupAuthoritative(inv);
            else ProcessPickupLocal(inv);
        }
    }

    private void ProcessPickupAuthoritative(InventoryController inv)
    {
        ApplyReward(inv);
        SpawnHardcodedEffect();
        SpawnPickupEffectClientRpc();

        NetworkObject netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            if (netObj.InScenePlaced)
            {
                netObj.Despawn(false);
                Destroy(gameObject);
            }
            else
            {
                netObj.Despawn(true);
            }
        }
    }

    private void ProcessPickupLocal(InventoryController inv)
    {
        ApplyReward(inv);
        SpawnHardcodedEffect();
        Destroy(gameObject);
    }

    private void ApplyReward(InventoryController inv)
    {
        if (item.expValue > 0f)
        {
            StatsController stats = inv != null ? inv.GetComponent<StatsController>() : null;
            if (stats != null) stats.AddExp(item.expValue);
            else if (StatsController.Instance != null) StatsController.Instance.AddExp(item.expValue);
        }
        else
        {
            if (inv != null) inv.AddItemServerRpc(item.itemId, 1);
            else InventoryController.Add(item);
        }
    }

    [ClientRpc]
    private void SpawnPickupEffectClientRpc()
    {
        if (IsOwner || IsServer) return;
        SpawnHardcodedEffect();
    }

    private void SpawnHardcodedEffect()
    {
        GameObject go = new GameObject("PickupEffect");
        go.transform.position = transform.position;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 0.5f;
        main.startSpeed = 5f;
        main.startSize = 0.2f;
        main.startColor = (item != null && item.expValue > 0) ? Color.yellow : Color.cyan;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0, 20) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;
        ps.Play();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        ActiveCount--;
        InventoryController.MarkCountDirty();
    }
}
