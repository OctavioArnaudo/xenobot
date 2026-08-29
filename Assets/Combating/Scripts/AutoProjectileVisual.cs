using UnityEngine;

namespace Combating.Scripts
{
    public class AutoProjectileVisual : MonoBehaviour
    {
        public Color ProjectileColor = new Color(0.1f, 0.85f, 1f, 1f);
        public float CoreSize = 0.12f;
        public float TrailTime = 0.12f;
        public float TrailWidth = 0.08f;

        void Awake()
        {
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "ProjectileVisual";
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * CoreSize;

            Collider coreCollider = core.GetComponent<Collider>();
            if (coreCollider != null)
                Destroy(coreCollider);

            Renderer renderer = core.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");

                if (shader != null)
                {
                    Material material = new Material(shader);
                    material.color = ProjectileColor;
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", ProjectileColor * 2f);
                    renderer.material = material;
                }
            }

            TrailRenderer trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = TrailTime;
            trail.startWidth = TrailWidth;
            trail.endWidth = 0f;
            Shader trailShader = Shader.Find("Sprites/Default");
            if (trailShader != null)
                trail.material = new Material(trailShader);
            trail.startColor = ProjectileColor;
            trail.endColor = new Color(ProjectileColor.r, ProjectileColor.g, ProjectileColor.b, 0f);
        }
    }
}
