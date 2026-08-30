using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Universal Pickup Controller for Xenobot.
/// Handles Inventory Items, Experience Orbs, and Network Synchronization.
/// Logic is on the Root, Visuals are on Children.
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
        // 1. Fallback Visuals: Si el raiz no tiene hijos configurados por el usuario, generar uno.
        if (transform.childCount == 0 && (item == null || item.worldPrefab == null))
        {
            GenerateFallbackVisuals();
        }

        // 2. Garantizar que el objeto Raiz tenga un Trigger bien posicionado para la recoleccion
        var col = GetComponent<Collider>();
        if (col == null)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.8f;
            sc.center = Vector3.up * 0.4f; // Centrar ligeramente arriba del pivote
        }
        else
        {
            col.isTrigger = true;

            // CORRECCION CRITICA: Resetear el centro si el collider viene de un prefab con offsets exagerados
            if (col is BoxCollider bc) bc.center = Vector3.up * 0.4f;
            else if (col is SphereCollider sc) sc.center = Vector3.up * 0.4f;
            else if (col is CapsuleCollider cc) cc.center = Vector3.up * 0.4f;
        }

        // 3. Garantizar Rigidbody (Kinematic) para que OnTriggerEnter funcione con CharacterController
        var rb = GetComponent<Rigidbody>();
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
        InventoryController.MarkCountDirty();
    }

    void Update()
    {
        // Movimiento de flotacion y rotacion (afecta a toda la jerarquia)
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
        _timer += Time.deltaTime * bobbingSpeed;
        transform.position = _startPos + new Vector3(0, Mathf.Sin(_timer) * bobbingAmount, 0);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_taken) return;

        // Deteccion robusta de cualquier tipo de Player
        // Buscamos componentes en el objeto que colisiona, en sus padres o por Tag
        var playerMovement = other.GetComponentInParent<Xenobot.Movement.PlayerController>();
        var playerModular = other.GetComponentInParent<Xenobot.Combat.Modular.PlayerController>();
        bool hasPlayerTag = other.CompareTag("Player") || (other.transform.parent != null && other.transform.parent.CompareTag("Player"));

        if (playerMovement != null || playerModular != null || hasPlayerTag)
        {
            _taken = true;
            ProcessPickup();
        }
        else
        {
            // Debug opcional para ver que esta tocando el trigger
            // Debug.Log($"[Pickup] Tocado por objeto no-jugador: {other.name} (Tag: {other.tag})");
        }
    }

    private void ProcessPickup()
    {
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
            StatsController.Instance?.AddExp(expAmount);
            Debug.Log($"[Pickup] Orbe de Experiencia (+{expAmount})");
        }

        if (particleEffectPrefab != null)
            Instantiate(particleEffectPrefab, transform.position, Quaternion.identity);

        if (IsNetworkActive)
        {
            if (IsServer)
            {
                NetworkObject.Despawn(false);
                Destroy(gameObject);
            }
            else gameObject.SetActive(false);
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

        PrimitiveType[] shapes = { PrimitiveType.Sphere, PrimitiveType.Cube, PrimitiveType.Capsule, PrimitiveType.Cylinder };
        PrimitiveType randomShape = shapes[Random.Range(0, shapes.Length)];

        GameObject visual = GameObject.CreatePrimitive(randomShape);
        visual.name = "Fallback_Mesh";
        visual.transform.SetParent(renderRoot.transform, false);

        float s = Random.Range(0.4f, 0.7f);
        visual.transform.localScale = new Vector3(s, s, s);
        visual.transform.localRotation = Random.rotation;

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
        InventoryController.MarkCountDirty();
    }
}
