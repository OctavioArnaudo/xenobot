using UnityEngine;

namespace Xenobot.ModularCombat
{
    public class AutoParticleBurst : MonoBehaviour
    {
        public Color StartColor = new Color(1f, 0.65f, 0.15f, 1f);
        public float StartSize = 0.18f;
        public float Lifetime = 0.35f;
        public int BurstCount = 18;
        public float Speed = 3.5f;
        public float DestroyAfter = 1.5f;

        void Awake()
        {
            ParticleSystem particles = gameObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = Lifetime;
            main.startSpeed = Speed;
            main.startSize = StartSize;
            main.startColor = StartColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)BurstCount) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.2f;

            particles.Play();
            Destroy(gameObject, DestroyAfter);
        }
    }
}
