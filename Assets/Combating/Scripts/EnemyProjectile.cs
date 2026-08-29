using UnityEngine;
using Unity.Netcode;

// Proyectil simple: esfera roja luminosa que viaja en lnea recta.
// Funciona en red (autoridad del servidor) y en offline.
public class EnemyProjectile : NetworkBehaviour
{
    public float lifeTime = 4f;
    public float radius = 0.15f;

    Vector3 m_Dir;
    float m_Speed;
    int m_Damage;

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool CanExecuteLogic => !IsNetworkActive || IsServer;

    void Awake()
    {
        // Esfera visual roja luminosa
        var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * radius * 2f;
        Destroy(visual.GetComponent<Collider>());

        var mr = visual.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", Color.red);
        mat.SetColor("_Color", Color.red);
        mr.material = mat;

        var light = gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.red;
        light.intensity = 2f;
        light.range = 2f;

        var col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = radius;
    }

    public void Launch(Vector3 direction, float speed, int damage)
    {
        m_Dir = direction.normalized;
        m_Speed = speed;
        m_Damage = damage;

        if (CanExecuteLogic)
            Destroy(gameObject, lifeTime);
    }

    void Update()
    {
        // Solo el servidor o el entorno local mueven el proyectil
        if (!CanExecuteLogic) return;
        transform.position += m_Dir * m_Speed * Time.deltaTime;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!CanExecuteLogic) return;
        if (!other.CompareTag("Player")) return;

        EnemyAI.DamagePlayer(other.transform, m_Damage);

        if (IsNetworkActive && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
        else
            Destroy(gameObject);
    }
}
