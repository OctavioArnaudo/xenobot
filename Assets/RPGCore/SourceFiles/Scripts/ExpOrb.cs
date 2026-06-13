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
        foreach (var c in GetComponents<Collider>()) Destroy(c);

        // Esfera visual en hijo
        var visual = new GameObject("Visual");
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * sphereRadius * 2f;

        var mf = visual.AddComponent<MeshFilter>();
        mf.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");

        var mr = visual.AddComponent<MeshRenderer>();

        // Unlit: siempre visible, ignora iluminación completamente
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Unlit/Color")
                  ?? Shader.Find("Standard");

        var mat = new Material(shader);
        // URP Unlit usa _BaseColor, Unlit/Color usa _Color
        mat.SetColor("_BaseColor", glowColor);
        mat.SetColor("_Color", glowColor);
        mr.material = mat;

        // Luz puntual
        var lt = gameObject.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = glowColor;
        lt.intensity = lightIntensity;
        lt.range = lightRange;

        // Collider
        var col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.center = Vector3.zero;
        col.radius = sphereRadius * 1.5f;
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
        if (_taken || !other.CompareTag("Player")) return;
        _taken = true;
        CharacterStats.Instance?.AddExp(expAmount);
        Destroy(gameObject);
    }
}