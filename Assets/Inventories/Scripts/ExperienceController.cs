using UnityEngine;

public class ExpOrb : MonoBehaviour
{
    [Header("EXP")]
    public float expAmount = 25f;

    [Header("Visual")]
    public float sphereRadius = 0.3f;
    public Color glowColor = new Color(1f, 0.85f, 0.1f, 1f);
    public float lightIntensity = 3f;
    public float lightRange = 4f;

    [Header("Movimiento")]
    public float bobSpeed = 2f;
    public float bobAmount = 0.15f;
    public float rotateSpeed = 90f;

    Vector3 _startPos;
    bool _taken;

    void Awake()
    {
        // Cleanup existing colliders
        foreach (var c in GetComponents<Collider>())
        {
            if (!c.isTrigger) Destroy(c);
        }

        // Crear Esfera Visual usando Primitivas de Unity para evitar fallos de Resources
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Visual";
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * sphereRadius * 2f;

        // Quitar el collider de la primitiva visual para que no estorbe
        if (visual.TryGetComponent<Collider>(out var cVis)) Destroy(cVis);

        var mr = visual.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Unlit/Color")
                  ?? Shader.Find("Standard");

        var mat = new Material(shader);
        mat.SetColor("_BaseColor", glowColor);
        mat.SetColor("_Color", glowColor);
        mr.material = mat;

        // Luz puntual (Configuración segura)
        Light lt = GetComponent<Light>();
        if (lt == null) lt = gameObject.AddComponent<Light>();

        if (lt != null)
        {
            lt.type = LightType.Point;
            lt.color = glowColor;
            lt.intensity = lightIntensity;
            lt.range = lightRange;
        }

        // Garantizar Collider Trigger
        SphereCollider col = GetComponent<SphereCollider>();
        if (col == null) col = gameObject.AddComponent<SphereCollider>();

        if (col != null)
        {
            col.isTrigger = true;
            col.center = Vector3.zero;
            col.radius = sphereRadius * 1.5f;
        }
    }

    void Start() => _startPos = transform.position;

    void Update()
    {
        transform.position = new Vector3(
            _startPos.x,
            _startPos.y + Mathf.Sin(Time.time * bobSpeed) * bobAmount,
            _startPos.z);
        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_taken) return;

        // Buscamos si el objeto que entró tiene el Tag Player
        if (other.CompareTag("Player"))
        {
            _taken = true;
            StatsController.Instance?.AddExp(expAmount);

            // Si estamos en red, el servidor debería manejar la destrucción,
            // pero como los orbes suelen ser locales o simples, Destroy es suficiente.
            Destroy(gameObject);
        }
    }
}