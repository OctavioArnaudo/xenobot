using UnityEngine;

/// <summary>
/// Specialized script to generate experience orb visuals procedurally.
/// Designed to be added to a child GameObject of a Pickup to handle its rendering.
/// </summary>
[ExecuteAlways]
public class ExperienceController : MonoBehaviour
{
    [Header("Orb Settings")]
    public float sphereRadius = 0.5f;
    public Color orbColor = new Color(1f, 0.85f, 0f); // Golden yellow
    public float lightIntensity = 2.5f;
    public float lightRange = 4f;

    void Awake()
    {
        GenerateOrbVisuals();
    }

    public void GenerateOrbVisuals()
    {
        // 1. Limpiar visuales previos para evitar duplicados en el hijo
        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("Xenobot_"))
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        // 2. Crear Esfera Visual como hijo de este objeto (que ya es un hijo del raiz)
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "Xenobot_ExpVisual_Mesh";
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * sphereRadius;

        // Eliminar el collider de la esfera visual (el collider debe estar en el objeto Raiz con el PickupController)
        if (visual.TryGetComponent<Collider>(out var c))
        {
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }

        // 3. Material con Emision
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

        // 4. Luz de apoyo (Configuracion segura)
        Light lt = GetComponent<Light>();
        if (lt == null) lt = gameObject.AddComponent<Light>();

        lt.type = LightType.Point;
        lt.color = orbColor;
        lt.intensity = lightIntensity;
        lt.range = lightRange;
    }
}
