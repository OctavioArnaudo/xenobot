using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Specialized controller for Key visual representation.
/// Generates a 3D door key mesh procedurally.
/// Requires NetworkObject and PickupController (pre-configured with Item_Key).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PickupController))]
public class KeyController : MonoBehaviour
{
    [Header("Visual Settings")]
    public Color keyColor = new Color(0.1f, 0.4f, 1f); // Bright Blue
    public float metallic = 0.8f;
    public float smoothness = 0.9f;

    void Awake()
    {
        SetupPickup();
        GenerateKeyVisuals();
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
            string path = "Assets/Crafting/Data/Item_Key.asset";
            pickup.item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (pickup.item != null) UnityEditor.EditorUtility.SetDirty(pickup);
#endif
        }
    }

    public void GenerateKeyVisuals()
    {
        foreach (Transform child in transform)
        {
            if (child.name == "KeyRender")
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        GameObject visual = new GameObject("KeyRender");
        visual.transform.SetParent(transform, false);
        visual.transform.localRotation = Quaternion.Euler(0, 0, 90); // Horizontal key

        MeshFilter mf = visual.AddComponent<MeshFilter>();
        MeshRenderer mr = visual.AddComponent<MeshRenderer>();

        // We will build the key using multiple primitives for simplicity in "hardcoding"
        // or a combined mesh. Let's create child parts for the visual.

        CreatePart(visual.transform, "Shaft", new Vector3(0.5f, 0.05f, 0.05f), new Vector3(0.25f, 0, 0));
        CreatePart(visual.transform, "Head", new Vector3(0.2f, 0.25f, 0.05f), new Vector3(-0.1f, 0, 0));
        CreatePart(visual.transform, "Tooth1", new Vector3(0.05f, 0.1f, 0.05f), new Vector3(0.4f, -0.05f, 0));
        CreatePart(visual.transform, "Tooth2", new Vector3(0.05f, 0.08f, 0.05f), new Vector3(0.5f, -0.04f, 0));

        // Apply material to all
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material mat = new Material(shader);
        mat.color = keyColor;
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

        foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterial = mat;
        }
    }

    private void CreatePart(Transform parent, string name, Vector3 scale, Vector3 pos)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localScale = scale;
        part.transform.localPosition = pos;

        if (part.TryGetComponent<Collider>(out var c))
        {
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }
    }
}
