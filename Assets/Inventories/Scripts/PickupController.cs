using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Universal Pickup Controller for Xenobot.
/// Handles Inventory Items, Experience Orbs, and Network Synchronization.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PickupController : NetworkBehaviour
{
    [Header("Data Configuration")]
    [Tooltip("Leave null if this is a pure experience orb")]
    public ItemData item;
    [Tooltip("Only used if 'item' is null")]
    public float expAmount = 25f;

    [Header("Visuals & Effects")]
    public GameObject particleEffectPrefab;
    public bool useAutoExperienceVisuals = true;

    [Header("Motion")]
    public float rotationSpeed = 100f;
    public float bobbingAmount = 0.15f;
    public float bobbingSpeed = 2f;

    private Vector3 _startPos;
    private float _timer;
    private bool _taken;
    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    void Awake()
    {
        // 1. Zero-Dependency Visuals for Experience Orbs
        if (item == null && useAutoExperienceVisuals)
        {
            SetupExperienceVisuals();
        }

        // 2. Garantizar que tenga un Trigger
        var col = GetComponent<Collider>();
        if (col == null) col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
    }

    void Start()
    {
        _startPos = transform.position;
        InventoryController.MarkCountDirty();
    }

    void Update()
    {
        // Movimiento de flotacion y rotacion
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
        _timer += Time.deltaTime * bobbingSpeed;
        transform.position = _startPos + new Vector3(0, Mathf.Sin(_timer) * bobbingAmount, 0);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_taken) return;

        // Deteccion robusta de cualquier tipo de Player
        bool isPlayer = other.GetComponentInParent<Xenobot.Movement.PlayerController>() != null ||
                        other.GetComponentInParent<Xenobot.Combat.Modular.PlayerController>() != null ||
                        other.CompareTag("Player");

        if (isPlayer)
        {
            _taken = true;
            ProcessPickup();
        }
    }

    private void ProcessPickup()
    {
        // 1. Logica de Recompensa
        if (item != null)
        {
            if (item.expValue > 0f)
            {
                StatsController.Instance?.AddExp(item.expValue);
                Debug.Log($"[Pickup] {item.displayName} (EXP: {item.expValue})");
            }
            else
            {
                InventoryController.Add(item);
                Debug.Log($"[Pickup] {item.displayName} añadido al inventario.");
            }
        }
        else
        {
            // Orbe de experiencia pura
            StatsController.Instance?.AddExp(expAmount);
            Debug.Log($"[Pickup] Orbe de Experiencia (+{expAmount})");
        }

        // 2. Efecto visual
        if (particleEffectPrefab != null)
            Instantiate(particleEffectPrefab, transform.position, Quaternion.identity);

        // 3. Sincronizacion de Red y Destruccion
        if (IsNetworkActive)
        {
            if (IsServer)
            {
                // Despawn(false) para objetos en escena evita warnings, luego destruimos manualmente
                NetworkObject.Despawn(false);
                Destroy(gameObject);
            }
            else gameObject.SetActive(false); // Ocultar localmente mientras el server procesa
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void SetupExperienceVisuals()
    {
        // Crear visual de orbe si no tiene nada
        if (transform.childCount > 0) return;

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Exp_Visual";
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * 0.5f;
        if (visual.TryGetComponent<Collider>(out var c)) Destroy(c);

        var mr = visual.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        mat.color = new Color(1f, 0.85f, 0f); // Dorado/Amarillo
        mr.material = mat;

        Light lt = gameObject.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = mat.color;
        lt.intensity = 2f;
        lt.range = 3f;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        InventoryController.MarkCountDirty();
    }
}
