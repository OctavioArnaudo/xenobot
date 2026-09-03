using UnityEngine;
using Combating.Scripts;

namespace Crafting.Scripts
{
    /// <summary>
    /// Specialized script to handle experience orb visuals and logic.
    /// Implements IItemFunctional to add EXP to the player.
    /// </summary>
    [ExecuteAlways]
    public class ExperienceController : MonoBehaviour, IItemFunctional
    {
        [Header("Functional Settings")]
        public float expAmount = 20f;

        [Header("Orb Settings")]
        public float sphereRadius = 0.5f;
        public Color orbColor = new Color(1f, 0.85f, 0f); // Golden yellow
        public float lightIntensity = 2.5f;
        public float lightRange = 4f;

        void Awake()
        {
            GenerateOrbVisuals();
        }

        public void ApplyEffect(GameObject player)
        {
            StatsController stats = player.GetComponent<StatsController>();
            if (stats == null) stats = StatsController.Instance;

            if (stats != null)
            {
                stats.AddExp(expAmount);
                Debug.Log($"[ExperienceController] Otorgados {expAmount} EXP al jugador.");
            }
        }

        public void GenerateOrbVisuals()
        {
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Xenobot_"))
                {
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                }
            }

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Xenobot_ExpVisual_Mesh";
            visual.transform.SetParent(transform, false);
            visual.transform.localScale = Vector3.one * sphereRadius;

            if (visual.TryGetComponent<Collider>(out var c))
            {
                if (Application.isPlaying) Destroy(c);
                else DestroyImmediate(c);
            }

            var mr = visual.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Standard");

            var mat = new Material(shader);
            mat.color = orbColor;
            if (shader.name.Contains("Lit") || shader.name.Contains("Standard"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", orbColor * 2.5f);
            }
            mr.sharedMaterial = mat;

            Light lt = GetComponent<Light>();
            if (lt == null) lt = gameObject.AddComponent<Light>();

            lt.type = LightType.Point;
            lt.color = orbColor;
            lt.intensity = lightIntensity;
            lt.range = lightRange;
        }
    }
}
