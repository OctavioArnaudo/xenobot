using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Specialized controller for Life (Heart) visual representation.
/// Generates a low-poly 3D heart mesh procedurally.
/// Requires NetworkObject and PickupController (pre-configured with Item_Life).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PickupController))]
public class LifeController : MonoBehaviour
{
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

        // Vertices for a stylized low-poly heart
        // 0: Bottom tip
        // 1: Left shoulder front, 2: Center indent front, 3: Right shoulder front
        // 4: Left shoulder back, 5: Center indent back, 6: Right shoulder back
        // 7: Top Left peak front, 8: Top Right peak front
        // 9: Top Left peak back, 10: Top Right peak back

        float w = 0.4f; // half width
        float h = 0.7f; // total height
        float d = 0.15f; // half depth
        float indent = 0.5f; // indent height

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(0, 0, 0),             // 0: Bottom Tip
            new Vector3(-w, 0.5f, d),        // 1: L Shoulder F
            new Vector3(0, indent, d),       // 2: C Indent F
            new Vector3(w, 0.5f, d),         // 3: R Shoulder F
            new Vector3(-w, 0.5f, -d),       // 4: L Shoulder B
            new Vector3(0, indent, -d),      // 5: C Indent B
            new Vector3(w, 0.5f, -d),        // 6: R Shoulder B
            new Vector3(-w*0.6f, h, d*0.5f), // 7: L Peak F
            new Vector3(w*0.6f, h, d*0.5f),  // 8: R Peak F
            new Vector3(-w*0.6f, h, -d*0.5f),// 9: L Peak B
            new Vector3(w*0.6f, h, -d*0.5f)  // 10: R Peak B
        };

        // Simplified triangles
        int[] triangles = new int[]
        {
            // Front Face
            0, 1, 2,  0, 2, 3,  1, 7, 2,  2, 8, 3,
            // Back Face
            0, 5, 4,  0, 6, 5,  4, 5, 9,  5, 6, 10,
            // Left Side
            0, 4, 1,  1, 4, 9,  1, 9, 7,
            // Right Side
            0, 3, 6,  3, 10, 6,  3, 8, 10,
            // Top/Indent
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
