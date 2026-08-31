using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Universal Pickup Controller for Xenobot.
/// Logic is on the Root, Visuals/Colliders can be anywhere in hierarchy.
/// Forces a valid pickup zone based on visual bounds.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PickupController : NetworkBehaviour
{
    [Header("Data Configuration")]
    public ItemData item;
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
        // 1. Asegurar Capa Correcta (Layer 0 es la mas segura para interacciones)
        if (gameObject.layer != 0) gameObject.layer = 0;

        // 2. Fallback Visuals
        if (transform.childCount == 0 && (item == null || item.worldPrefab == null))
        {
            GenerateFallbackVisuals();
        }

        // 3. Forzar un Trigger Maestro basado en el renderizado real
        CreateMasterTrigger();

        // 4. Garantizar Rigidbody (Kinematic) para detección con CharacterController
        var rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    private void CreateMasterTrigger()
    {
        // Limpiar o convertir colisionadores existentes
        foreach (var c in GetComponentsInChildren<Collider>()) c.isTrigger = true;

        // Calcular el area real que ocupan los visuales
        Renderer[] renders = GetComponentsInChildren<Renderer>();
        if (renders.Length > 0)
        {
            Bounds b = renders[0].bounds;
            foreach (var r in renders) b.Encapsulate(r.bounds);

            // Añadir un SphereCollider en la RAIZ que siempre cubra el centro del render
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;

            // Convertimos la posicion global del centro del render a local de la raiz
            sc.center = transform.InverseTransformPoint(b.center);

            // Radio: el extents mas grande + un margen de facilidad (1.5x)
            float maxDim = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
            sc.radius = Mathf.Max(0.8f, maxDim * 1.5f);
        }
        else
        {
            // Fallback si no hay renders
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 1f;
            sc.center = Vector3.up * 0.5f;
        }
    }

    void Start()
    {
        _startPos = transform.position;
        InventoryController.MarkCountDirty();
    }

    void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
        _timer += Time.deltaTime * bobbingSpeed;
        transform.position = _startPos + new Vector3(0, Mathf.Sin(_timer) * bobbingAmount, 0);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_taken) return;

        // Deteccion robusta de cualquier tipo de Player
        var playerMovement = other.GetComponentInParent<Xenobot.Movement.PlayerController>();
        var playerModular = other.GetComponentInParent<Xenobot.Combat.Modular.PlayerController>();
        bool hasPlayerTag = other.CompareTag("Player") || (other.transform.parent != null && other.transform.parent.CompareTag("Player"));

        if (playerMovement != null || playerModular != null || hasPlayerTag)
        {
            _taken = true;
            ProcessPickup();
        }
    }

    private void ProcessPickup()
    {
        if (item != null)
        {
            if (item.expValue > 0f) StatsController.Instance?.AddExp(item.expValue);
            else InventoryController.Add(item);
        }
        else StatsController.Instance?.AddExp(expAmount);

        if (particleEffectPrefab != null) Instantiate(particleEffectPrefab, transform.position, Quaternion.identity);

        if (IsNetworkActive)
        {
            if (IsServer)
            {
                NetworkObject.Despawn(false);
                Destroy(gameObject);
            }
            else gameObject.SetActive(false);
        }
        else Destroy(gameObject);
    }

    private void GenerateFallbackVisuals()
    {
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
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        InventoryController.MarkCountDirty();
    }
}
