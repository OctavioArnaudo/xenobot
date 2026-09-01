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
    private const float PICKUP_DELAY = 0.3f; // Reducido el delay

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    void Awake()
    {
        _spawnTime = Time.time;
        CreateMasterTrigger();
    }

    private void CreateMasterTrigger()
    {
        // Asegurar que haya un trigger para detectar al jugador
        Collider[] colliders = GetComponents<Collider>();
        bool hasTrigger = false;
        foreach (var c in colliders)
        {
            if (c.isTrigger) { hasTrigger = true; break; }
        }

        if (!hasTrigger)
        {
            SphereCollider sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.7f; // Aumentado un poco el radio
        }

        // Asegurar Rigidbody para detección confiable con CharacterControllers
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
        _spawnTime = Time.time; // Reiniciar por si hubo delay en el spawn
        ActiveCount++;
        InventoryController.MarkCountDirty();
    }

    void Update()
    {
        // Movimiento de flotacion y rotacion (afecta a toda la jerarquia)
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

        // Si tiene un rigidbody NO kinematico (esta volando por fisica), actualizamos _startPos
        if (TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic)
        {
            _startPos = transform.position;
        }
        else
        {
            _timer += Time.deltaTime * bobbingSpeed;
            transform.position = _startPos + new Vector3(0, Mathf.Sin(_timer) * bobbingAmount, 0);
        }
    }

    void OnTriggerStay(Collider other)
    {
        // Redirigir a OnTriggerEnter para manejar el caso de que el jugador ya esté encima al spawnear
        OnTriggerEnter(other);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_taken || Time.time < _spawnTime + PICKUP_DELAY) return;

        // Solo el servidor tiene autoridad sobre la recoleccion real
        if (IsNetworkActive && !IsServer) return;

        InventoryController inv = other.GetComponentInParent<InventoryController>();
        bool isPlayer = other.CompareTag("Player") || inv != null;

        if (isPlayer)
        {
            Debug.Log($"[Pickup] Detección de jugador confirmada en {gameObject.name}");

            if (item == null)
            {
                Debug.LogWarning($"[Pickup] {gameObject.name} detectó al jugador pero no tiene ItemData.");
                return;
            }

            _taken = true;
            if (IsNetworkActive) ProcessPickupAuthoritative(inv);
            else ProcessPickupLocal(inv);
        }
    }

    private void ProcessPickupAuthoritative(InventoryController inv)
    {
        // 1. Logica de Recompensa
        ApplyReward(inv);

        // 2. Efecto visual
        SpawnHardcodedEffect();
        SpawnPickupEffectClientRpc();

        // 3. Despawn
        GetComponent<NetworkObject>().Despawn(true);
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
            if (inv != null)
            {
                inv.AddItemServerRpc(item.itemId, 1);
            }
            else
            {
                InventoryController.Add(item);
            }
        }
    }

    [ClientRpc]
    private void SpawnPickupEffectClientRpc()
    {
        if (IsOwner || IsServer) return; // Ya se ejecutó localmente o es el server
        SpawnHardcodedEffect();
    }

    private void SpawnHardcodedEffect()
    {
        // Crear un efecto de partículas simple sin necesidad de prefab
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
