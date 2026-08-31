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
    public float expAmount = 25f;

    public static int ActiveCount { get; private set; }

    [Header("Visuals & Effects")]
    public GameObject particleEffectPrefab;

    [Header("Motion")]
    public float rotationSpeed = 100f;
    public float bobbingAmount = 0.15f;
    public float bobbingSpeed = 2f;

    private Vector3 _startPos;
    private float _timer;
    private bool _taken;
    private float _spawnTime;
    private const float PICKUP_DELAY = 0.8f; // Delay para evitar auto-recogida al dropear

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    void Awake()
    {
        // ... (resto de Awake igual)
        _spawnTime = Time.time;
    }

    // ... (resto de CreateMasterTrigger igual)

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

    void OnTriggerEnter(Collider other)
    {
        if (_taken || Time.time < _spawnTime + PICKUP_DELAY) return;

        // Deteccion robusta de cualquier tipo de Player
        // ... (resto de OnTriggerEnter igual)
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
            if (StatsController.Instance != null)
            {
                StatsController.Instance.AddExp(expAmount);
                Debug.Log($"[Pickup] Orbe de Experiencia (+{expAmount})");
            }
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

    private void GenerateFallbackVisuals()
    {
        // Crear un objeto hijo para el renderizado de respaldo
        GameObject renderRoot = new GameObject("Xenobot_FallbackRender");
        renderRoot.transform.SetParent(transform, false);

        PrimitiveType[] shapes = { PrimitiveType.Sphere, PrimitiveType.Cube, PrimitiveType.Capsule };
        GameObject visual = GameObject.CreatePrimitive(shapes[Random.Range(0, shapes.Length)]);
        visual.name = "Fallback_Mesh";
        visual.transform.SetParent(renderRoot.transform, false);

        float s = Random.Range(0.4f, 0.7f);
        visual.transform.localScale = new Vector3(s, s, s);
        if (visual.TryGetComponent<Collider>(out var c)) Destroy(c);

        var mr = visual.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader);

        Color randomColor = new Color(Random.value, Random.value, Random.value);
        mat.color = randomColor;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", randomColor * 1.8f);
        mr.sharedMaterial = mat;

        Light lt = renderRoot.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = randomColor;
        lt.intensity = 1.5f;
        lt.range = 3f;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        ActiveCount--;
        InventoryController.MarkCountDirty();
    }
}
