using UnityEngine;
using Unity.Netcode;
using Combating.Scripts;

namespace Crafting.Scripts
{
    /// <summary>
    /// Specialized controller for Life (Heart) visual representation and logic.
    /// Handles procedural mesh generation and healing effect.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(NetworkObject))]
    public class LifeController : MonoBehaviour, IItemFunctional
    {
        // Economy Reliability Constants
        private const int HEAL_AMOUNT = 30;

        [Header("Visual Settings")]
        public Color heartColor = new Color(0.6f, 0f, 0f); // Dark Red
        public Color emissionColor = new Color(1f, 0.1f, 0.1f); // Bright Red Glow
        public float emissionIntensity = 2.0f;

        void Awake()
        {
            SetupPickup();
            GenerateLifeVisuals();
        }

        void OnValidate()
        {
            SetupPickup();
        }

        private void SetupPickup()
        {
            var pickup = GetComponent<PickupController>();
            if (pickup != null && pickup.item == null)
            {
#if UNITY_EDITOR
                string path = "Assets/Crafting/Data/Item_Life.asset";
                pickup.item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (pickup.item != null) UnityEditor.EditorUtility.SetDirty(pickup);
#endif
            }
        }

        public void ApplyEffect(GameObject entity)
        {
            HealController heal = entity.GetComponent<HealController>();
            if (heal == null)
            {
                var hub = entity.GetComponent<ModularController>() ?? entity.GetComponentInParent<ModularController>();
                if (hub != null) heal = hub.GetModule<HealController>();
            }

            if (heal != null)
            {
                heal.Heal(HEAL_AMOUNT);
            }
        }

        public void GenerateLifeVisuals()
        {
            foreach (Transform child in transform)
            {
                if (child.name == "LifeRender")
                {
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                }
            }

            GameObject visual = new GameObject("LifeRender");
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.zero;

            MeshFilter mf = visual.AddComponent<MeshFilter>();
            MeshRenderer mr = visual.AddComponent<MeshRenderer>();

            Mesh mesh = new Mesh();
            mesh.name = "Heart_Mesh";

            float w = 0.4f;
            float h = 0.7f;
            float d = 0.15f;
            float indent = 0.5f;

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(0, 0, 0),
                new Vector3(-w, 0.5f, d),
                new Vector3(0, indent, d),
                new Vector3(w, 0.5f, d),
                new Vector3(-w, 0.5f, -d),
                new Vector3(0, indent, -d),
                new Vector3(w, 0.5f, -d),
                new Vector3(-w*0.6f, h, d*0.5f),
                new Vector3(w*0.6f, h, d*0.5f),
                new Vector3(-w*0.6f, h, -d*0.5f),
                new Vector3(w*0.6f, h, -d*0.5f)
            };

            int[] triangles = new int[]
            {
                0, 1, 2,  0, 2, 3,  1, 7, 2,  2, 8, 3,
                0, 5, 4,  0, 6, 5,  4, 5, 9,  5, 6, 10,
                0, 4, 1,  1, 4, 9,  1, 9, 7,
                0, 3, 6,  3, 10, 6,  3, 8, 10,
                7, 9, 5,  7, 5, 2,  8, 2, 5,  8, 5, 10
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mf.sharedMesh = mesh;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = new Material(shader);
            mat.color = heartColor;

            if (shader.name.Contains("Lit") || shader.name.Contains("Standard"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emissionColor * emissionIntensity);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.2f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.8f);
            }
            mr.sharedMaterial = mat;

            Light lt = visual.GetComponent<Light>();
            if (lt == null) lt = visual.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = emissionColor;
            lt.intensity = 1.5f;
            lt.range = 3f;
        }
    }
}
