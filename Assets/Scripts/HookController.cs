using UnityEngine;
using Unity.Netcode;

namespace Runtime.Scripts.Hook
{

    public class HookController : NetworkBehaviour
    {
        [Header("Configuración General")]
        [SerializeField] private bool generateOnStart = true;

        [Header("Paleta de Colores")]
        [SerializeField] private Color bodyColor = new Color(0.12f, 0.14f, 0.18f);      // Azul/Gris Metálico Oscuro
        [SerializeField] private Color metallicDetailsColor = new Color(0.35f, 0.38f, 0.42f); // Metal claro
        [SerializeField] private Color copperColor = new Color(0.8f, 0.38f, 0.15f);     // Cables de Cobre
        [SerializeField] private Color gripColor = new Color(0.08f, 0.08f, 0.09f);      // Goma Negra
        [SerializeField] private Color glowColor = new Color(0.0f, 0.85f, 1.0f) * 3.5f; // Cyan Neón Emisivo

        private GameObject rifleRoot;

        void Start()
        {
            if (generateOnStart)
            {
                BuildDetailedRifleWithHook();
            }
        }

        [ContextMenu("Reconstruir Rifle")]
        public void BuildDetailedRifleWithHook()
        {
            if (rifleRoot != null)
            {
                DestroyImmediate(rifleRoot);
            }

            // Materiales base
            Material matBody = CreateLitMaterial(bodyColor, 0.8f, 0.3f);
            Material matMetal = CreateLitMaterial(metallicDetailsColor, 0.9f, 0.4f);
            Material matCopper = CreateLitMaterial(copperColor, 0.85f, 0.35f);
            Material matGrip = CreateLitMaterial(gripColor, 0.1f, 0.8f);
            Material matGlow = CreateEmissionMaterial(glowColor);

            // 1. Contenedor principal
            rifleRoot = new GameObject("DetailedHookRifle_Root");
            rifleRoot.transform.SetParent(transform);
            rifleRoot.transform.localPosition = Vector3.zero;
            rifleRoot.transform.localRotation = Quaternion.identity;

            // 2. Chasis Principal (Receptor y Cuerpo)
            CreatePart("ChasisReceiver", PrimitiveType.Cube, rifleRoot.transform,
                new Vector3(0f, 0.05f, 0.35f), new Vector3(0.18f, 0.22f, 0.7f), matBody);

            CreatePart("ReceiverTopCover", PrimitiveType.Cube, rifleRoot.transform,
                new Vector3(0f, 0.18f, 0.35f), new Vector3(0.16f, 0.08f, 0.65f), matMetal);

            // 3. Empuñadura ergonómica
            Transform grip = CreatePart("Grip", PrimitiveType.Cube, rifleRoot.transform,
                new Vector3(0f, -0.15f, 0.1f), new Vector3(0.1f, 0.28f, 0.14f), matGrip);
            grip.localRotation = Quaternion.Euler(22f, 0f, 0f);

            // 4. Culata Táctica Posterior
            CreatePart("StockBase", PrimitiveType.Cube, rifleRoot.transform,
                new Vector3(0f, 0.02f, -0.15f), new Vector3(0.12f, 0.18f, 0.35f), matBody);

            CreatePart("StockPad", PrimitiveType.Cube, rifleRoot.transform,
                new Vector3(0f, -0.01f, -0.32f), new Vector3(0.13f, 0.24f, 0.06f), matGrip);

            // 5. Guardamanos Ranurado / Cañón Principal
            CreatePart("Handguard", PrimitiveType.Cylinder, rifleRoot.transform,
                new Vector3(0f, 0.05f, 0.95f), new Vector3(0.2f, 0.45f, 0.2f), matBody, new Vector3(90f, 0f, 0f));

            CreatePart("InnerBarrel", PrimitiveType.Cylinder, rifleRoot.transform,
                new Vector3(0f, 0.05f, 1.1f), new Vector3(0.12f, 0.65f, 0.12f), matMetal, new Vector3(90f, 0f, 0f));

            // 6. Riel Picatinny Superior
            CreatePart("TopRail", PrimitiveType.Cube, rifleRoot.transform,
                new Vector3(0f, 0.23f, 0.65f), new Vector3(0.08f, 0.03f, 1.1f), matMetal);

            // 7. Mira Réflex Integrada
            CreatePart("SightBase", PrimitiveType.Cube, rifleRoot.transform,
                new Vector3(0f, 0.27f, 0.3f), new Vector3(0.1f, 0.06f, 0.18f), matMetal);
            CreatePart("SightGlassFrame", PrimitiveType.Cube, rifleRoot.transform,
                new Vector3(0f, 0.34f, 0.33f), new Vector3(0.09f, 0.1f, 0.03f), matGrip);
            CreatePart("SightReticleGlow", PrimitiveType.Cube, rifleRoot.transform,
                new Vector3(0f, 0.34f, 0.33f), new Vector3(0.03f, 0.03f, 0.01f), matGlow);

            // 8. Núcleo Mecánico y Cables de Cobre (Mecanismo del Cabrestante/Winch)
            CreatePart("WinchCoreGlow", PrimitiveType.Cylinder, rifleRoot.transform,
                new Vector3(0f, 0.05f, 0.45f), new Vector3(0.15f, 0.12f, 0.15f), matGlow, new Vector3(0f, 0f, 90f));

            // Cables metálicos/cobre laterales
            CreatePart("CopperPipe_L", PrimitiveType.Cylinder, rifleRoot.transform,
                new Vector3(-0.11f, 0.08f, 0.58f), new Vector3(0.03f, 0.25f, 0.03f), matCopper, new Vector3(80f, -15f, 0f));
            CreatePart("CopperPipe_R", PrimitiveType.Cylinder, rifleRoot.transform,
                new Vector3(0.11f, 0.08f, 0.58f), new Vector3(0.03f, 0.25f, 0.03f), matCopper, new Vector3(80f, 15f, 0f));

            // 9. BOCA DEL CAÑÓN Y GANCHO DE 4 GARRAS (Hook Head)
            Transform hookBase = new GameObject("HookBase").transform;
            hookBase.SetParent(rifleRoot.transform);
            hookBase.localPosition = new Vector3(0f, 0.05f, 1.75f);

            CreatePart("HookCenterHub", PrimitiveType.Cylinder, hookBase,
                Vector3.zero, new Vector3(0.09f, 0.15f, 0.09f), matMetal, new Vector3(90f, 0f, 0f));

            CreatePart("CableNodeGlow", PrimitiveType.Sphere, hookBase,
                new Vector3(0f, 0f, 0.12f), new Vector3(0.07f, 0.07f, 0.07f), matGlow);

            // Generar 4 Garras anguladas alrededor del centro
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                Quaternion rotation = Quaternion.Euler(0f, 0f, angle);

                GameObject clawPivot = new GameObject($"ClawPivot_{i}");
                clawPivot.transform.SetParent(hookBase);
                clawPivot.transform.localPosition = Vector3.zero;
                clawPivot.transform.localRotation = rotation;

                // Brazo base de la garra
                CreatePart("ClawBase", PrimitiveType.Cube, clawPivot.transform,
                    new Vector3(0f, 0.12f, 0.05f), new Vector3(0.04f, 0.18f, 0.05f), matMetal, new Vector3(-25f, 0f, 0f));

                // Punta curva inclinada hacia adentro
                CreatePart("ClawTip", PrimitiveType.Cube, clawPivot.transform,
                    new Vector3(0f, 0.22f, 0.18f), new Vector3(0.035f, 0.15f, 0.04f), matMetal, new Vector3(45f, 0f, 0f));
            }
        }

        private Transform CreatePart(string name, PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, Vector3? localEuler = null)
        {
            GameObject obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent);
            obj.transform.localPosition = localPos;
            obj.transform.localScale = localScale;
            if (localEuler.HasValue)
            {
                obj.transform.localRotation = Quaternion.Euler(localEuler.Value);
            }

            // Eliminar colisionadores innecesarios
            Collider col = obj.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Asignar material
            Renderer rend = obj.GetComponent<Renderer>();
            if (rend != null && mat != null)
            {
                rend.material = mat;
            }

            return obj.transform;
        }

        private Material CreateLitMaterial(Color color, float metallic, float smoothness)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            Material mat = new Material(litShader);
            mat.color = color;

            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

            return mat;
        }

        private Material CreateEmissionMaterial(Color emissiveColor)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            Material mat = new Material(litShader);
            mat.color = emissiveColor;

            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissiveColor);

            return mat;
        }
    }
}