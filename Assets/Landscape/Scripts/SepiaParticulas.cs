using UnityEngine;

/// <summary>
/// Lluvia de partículas rojas que cae desde arriba del player.
/// El emitter es una caja plana y ancha flotando sobre el player —
/// se mueve con él, así la lluvia cubre siempre lo que la cámara ve.
/// </summary>
public class BiomaParticles : MonoBehaviour
{
    [Header("Lluvia")]
    [Tooltip("Ancho y largo del área de emisión (metros)")]
    public float areaSize = 50f;
    [Tooltip("Altura del emitter sobre el player")]
    public float heightAbove = 18f;
    [Tooltip("Velocidad de caída")]
    public float fallSpeed = 6f;

    [Header("Partículas")]
    public int count = 400;
    public float minSize = 0.08f;
    public float maxSize = 0.25f;
    public Color color = new Color(1f, 0.05f, 0.02f, 0.9f);

    Transform _player;
    ParticleSystem _ps;

    void Start()
    {
        _ps = gameObject.AddComponent<ParticleSystem>();

        var main = _ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = heightAbove / fallSpeed + 1f; // tiempo exacto hasta el suelo
        main.startSpeed = fallSpeed;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = color;
        main.maxParticles = count;
        main.gravityModifier = 1f;           // caída natural con física
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        // Local: todo el volumen (emitter + partículas) se mueve con el transform
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var em = _ps.emission;
        em.rateOverTime = count / main.startLifetime.constant;

        // Caja plana y ancha — emisión distribuida en toda el área horizontal
        var sh = _ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(areaSize, 0.1f, areaSize); // plana en Y

        // Dirección fija hacia abajo
        var vel = _ps.velocityOverLifetime;
        vel.enabled = false;

        var rend = _ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.material = MakeMaterial();

        _ps.Play();
    }

    void LateUpdate()
    {
        // Encontrar player una sola vez
        if (_player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) _player = go.transform;
        }

        // Emitter flota sobre el player
        if (_player != null)
            transform.position = _player.position + Vector3.up * heightAbove;
    }

    Material MakeMaterial()
    {
        string[] shaders = {
            "Legacy Shaders/Particles/Additive",
            "Legacy Shaders/Particles/Alpha Blended",
            "Mobile/Particles/Additive",
            "Sprites/Default",
        };
        foreach (var n in shaders)
        {
            var sh = Shader.Find(n);
            if (sh != null) { var m = new Material(sh); m.color = color; return m; }
        }
        return new Material(Shader.Find("Standard")) { color = color };
    }
}