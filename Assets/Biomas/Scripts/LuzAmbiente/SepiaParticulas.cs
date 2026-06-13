// SepiaParticulas.cs
using UnityEngine;

public class SepiaParticulas : MonoBehaviour
{
    [Range(50, 500)] public int cantidad = 200;
    [Range(0.01f, 0.2f)] public float velocidad = 0.04f;
    [Range(1f, 20f)] public float radio = 8f;
    [Range(0f, 1f)] public float opacidad = 0.35f;

    ParticleSystem _ps;

    void Start() => Construir();

    void Construir()
    {
        _ps = gameObject.AddComponent<ParticleSystem>();

        // ?? Main ??????????????????????????????????????????
        var main = _ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 14f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(velocidad * 0.5f, velocidad);
        main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.06f);
        main.maxParticles = cantidad;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // color sepia–rojizo con variación
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.65f, 0.20f, 0.08f, opacidad),
            new Color(0.40f, 0.12f, 0.05f, opacidad * 0.5f)
        );

        // ?? Emission ??????????????????????????????????????
        var em = _ps.emission;
        em.rateOverTime = cantidad / 12f;

        // ?? Shape: esfera para polvo ambiental ????????????
        var sh = _ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = radio;

        // ?? Velocity over lifetime: deriva suave ??????????
        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-velocidad, velocidad);
        vel.y = new ParticleSystem.MinMaxCurve(velocidad * 0.2f, velocidad * 0.6f);
        vel.z = new ParticleSystem.MinMaxCurve(-velocidad, velocidad);

        // ?? Fade in/out ???????????????????????????????????
        var col = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]{ new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f),
                   new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = grad;

        // ?? Size over lifetime: partículas más pequeñas al final
        var siz = _ps.sizeOverLifetime;
        siz.enabled = true;
        var szCurve = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.5f, 0.7f), new Keyframe(1f, 0.3f)
        );
        siz.size = new ParticleSystem.MinMaxCurve(1f, szCurve);

        _ps.Play();
    }
}