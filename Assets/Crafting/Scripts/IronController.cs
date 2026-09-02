using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Specialized controller for Iron Ingot visual representation.
/// Generates a 3D trapezoid (ingot) mesh procedurally.
/// Requires NetworkObject and PickupController (pre-configured with Item_Iron).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PickupController))]
public class IronController : MonoBehaviour
{
    [Header("Visual Settings")]
    public Color ironColor = new Color(0.75f, 0.75f, 0.8f); // Shiny Metallic Gray
    public float metallic = 0.9f;
    public float smoothness = 0.85f;

    void Awake()
    {
        SetupPickup();
        GenerateIronVisuals();
    }

    void OnValidate()
    {
        // En el editor, asegurar que el PickupController tenga el Item_Iron asignado
        SetupPickup();
    }

    private void SetupPickup()
    {
        var pickup = GetComponent<PickupController>();
        if (pickup != null && pickup.item == null)
        {
#if UNITY_EDITOR
            // Intentar cargar Item_Iron desde la ruta conocida
            string path = "Assets/Crafting/Data/Item_Iron.asset";
            pickup.item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (pickup.item != null)
            {
                UnityEditor.EditorUtility.SetDirty(pickup);
            }
#endif
        }
    }

    public void GenerateIronVisuals()
    {
        // 1. Limpiar visuales previos
        foreach (Transform child in transform)
        {
            if (child.name == "IronRender")
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        // 2. Crear Objeto Visual
        GameObject visual = new GameObject("IronRender");
        visual.transform.SetParent(transform, false);

        // Ajustar posición para que la base repose en el pivot
        visual.transform.localPosition = Vector3.zero;

        MeshFilter mf = visual.AddComponent<MeshFilter>();
        MeshRenderer mr = visual.AddComponent<MeshRenderer>();

        // 3. Crear Malla de Lingote (Trapecio 3D)
        Mesh mesh = new Mesh();
        mesh.name = "IronIngot_Mesh";

        // Definición de vértices para un lingote (trapezoide truncado)
        // Dimensiones incrementadas x4: Base 2.4x1.2, Top 1.6x0.8, Altura 1.0
        float bw = 1.2f, bd = 0.6f; // half-width/depth base
        float tw = 0.8f, td = 0.4f;  // half-width/depth top
        float h = 1.0f;

        Vector3[] vertices = new Vector3[]
        {
            // Bottom (0-3)
            new Vector3(-bw, 0, -bd), new Vector3(bw, 0, -bd), new Vector3(bw, 0, bd), new Vector3(-bw, 0, bd),
            // Top (4-7)
            new Vector3(-tw, h, -td), new Vector3(tw, h, -td), new Vector3(tw, h, td), new Vector3(-tw, h, td)
        };

        // Triángulos (sentido horario para normales hacia afuera)
        int[] triangles = new int[]
        {
            // Bottom
            0, 2, 1, 0, 3, 2,
            // Top
            4, 5, 6, 4, 6, 7,
            // Front (Face Z-)
            0, 1, 5, 0, 5, 4,
            // Back (Face Z+)
            2, 3, 7, 2, 7, 6,
            // Right (Face X+)
            1, 2, 6, 1, 6, 5,
            // Left (Face X-)
            3, 0, 4, 3, 4, 7
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        // 4. Material Metálico Brillante
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.name = "IronIngot_Material";
        mat.color = ironColor;

        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        else if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

        mr.sharedMaterial = mat;
    }
}
