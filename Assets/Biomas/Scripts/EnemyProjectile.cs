using UnityEngine;
using Unity.Netcode;

// Proyectil simple: esfera roja luminosa que viaja en línea recta.
// Se construye 100% por código, sin depender de un prefab visual externo.
public class EnemyProjectile : NetworkBehaviour
{
    public float lifeTime = 4f;
    public float radius = 0.15f;

    Vector3 m_Dir;
    float m_Speed;
    int m_Damage;

    void Awake()
    {
        // Esfera visual roja luminosa (Unlit, mismo criterio que ExpOrb.cs)
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
        if (IsServer) Destroy(gameObject, lifeTime);
    }

    void Update()
    {
        if (!IsServer) return;
        transform.position += m_Dir * m_Speed * Time.deltaTime;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (!other.CompareTag("Player")) return;

        EnemyAI.DamagePlayer(other.transform, m_Damage);
        if (NetworkObject.IsSpawned) NetworkObject.Despawn();
    }
}