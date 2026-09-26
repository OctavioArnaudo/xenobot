using UnityEngine;
using Unity.Netcode;
using Combating.Scripts;

namespace Crafting.Scripts
{
    /// <summary>
    /// Controller for Ammo visual representation and reload logic.
    /// Implements IItemFunctional, IItemUseAction, and IItemPickupAction to restore ammunition to ShootController.
    /// </summary>
    [ExecuteAlways]
    public class AmmoController : MonoBehaviour, IItemUseAction, IItemPickupAction
    {
        private const int AMMO_AMOUNT = 30;

        [Header("Visual Settings")]
        public bool generateDefaultVisuals = true;
        public Color brassColor = new Color(0.9f, 0.7f, 0.2f); // Gold / Brass
        public Color tipColor = new Color(0.8f, 0.2f, 0.1f);   // Red tracer tip

        void Awake()
        {
            SetupPickup();
            if (generateDefaultVisuals) GenerateAmmoVisuals();
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
                string path = "Assets/Crafting/Data/Item_Ammo.asset";
                pickup.item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (pickup.item != null) UnityEditor.EditorUtility.SetDirty(pickup);
#endif
            }
        }

        public void OnUseItem(GameObject entity) => ApplyEffect(entity);
        public void OnPickupItem(GameObject entity) => ApplyEffect(entity);

        public void ApplyEffect(GameObject entity)
        {
            var shooter = entity.GetComponent<ShootController>() ??
                          entity.GetComponentInParent<ShootController>() ??
                          entity.GetComponentInChildren<ShootController>();

            if (shooter != null)
            {
                shooter.AddAmmo(AMMO_AMOUNT);
                Debug.Log($"<color=yellow>[AmmoItem]</color> Munición restaurada (+{AMMO_AMOUNT}) en {entity.name}.");
            }
            else
            {
                Debug.LogWarning($"[AmmoItem] No se encontró ShootController en {entity.name} para recargar munición.");
            }
        }

        public void GenerateAmmoVisuals()
        {
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("AmmoRender"))
                {
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                }
            }

            // Create a stylized ammo box with bullet cylinders
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "AmmoRender";
            box.transform.SetParent(transform, false);
            box.transform.localScale = new Vector3(0.5f, 0.35f, 0.4f);
            DestroyColliders(box);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var boxMat = new Material(shader);
            boxMat.color = new Color(0.2f, 0.3f, 0.2f); // Military green
            box.GetComponent<MeshRenderer>().sharedMaterial = boxMat;

            // Add 3 decorative bullets sticking out
            for (int i = -1; i <= 1; i++)
            {
                GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                bullet.name = $"Ammo_Bullet_{i}";
                bullet.transform.SetParent(box.transform, false);
                bullet.transform.localPosition = new Vector3(i * 0.28f, 0.6f, 0f);
                bullet.transform.localScale = new Vector3(0.18f, 0.4f, 0.18f);
                DestroyColliders(bullet);

                var bulletMat = new Material(shader);
                bulletMat.color = brassColor;
                if (bulletMat.HasProperty("_Metallic")) bulletMat.SetFloat("_Metallic", 0.8f);
                if (bulletMat.HasProperty("_Smoothness")) bulletMat.SetFloat("_Smoothness", 0.7f);
                bullet.GetComponent<MeshRenderer>().sharedMaterial = bulletMat;

                // Bullet Tip
                GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                tip.name = $"Ammo_Tip_{i}";
                tip.transform.SetParent(bullet.transform, false);
                tip.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                tip.transform.localScale = new Vector3(0.95f, 0.6f, 0.95f);
                DestroyColliders(tip);

                var tipMat = new Material(shader);
                tipMat.color = tipColor;
                tip.GetComponent<MeshRenderer>().sharedMaterial = tipMat;
            }
        }

        private void DestroyColliders(GameObject go)
        {
            if (go.TryGetComponent<Collider>(out var c))
            {
                if (Application.isPlaying) Destroy(c);
                else DestroyImmediate(c, true);
            }
        }
    }
}
