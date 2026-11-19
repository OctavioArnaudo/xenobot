using UnityEngine;
using Unity.Netcode;
using Runtime.Scripts.Items;
using Runtime.Scripts.Pickup;
using Runtime.Scripts.Health;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Effective;

namespace Runtime.Scripts.Crystals
{
    [RequireComponent(typeof(NetworkObject))]
    public class CrystalController : NetworkBehaviour, IItemUseAction, IItemDropAction, IItemPickupAction
    {
        private const float DEFAULT_SCALE = 2.0f;
        public float RenderScale = 2.0f;
        public Optional<float> renderScale;
        public float EffectiveRenderScale => renderScale.Get(RenderScale, DEFAULT_SCALE);

        void Awake()
        {
            SetupPickup();
        }

        private void SetupPickup()
        {
            var pickup = GetComponent<PickupController>();
            if (pickup != null && pickup.item == null)
            {
#if UNITY_EDITOR
                string path = "Assets/Resources/Crafting/Items/Data/Item_Crystal.asset";
                pickup.item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemsData>(path);
#endif
            }
        }

        public void OnUseItem(GameObject entity)
        {
            var health = entity.GetComponent<HealthController>() ?? entity.GetComponentInParent<HealthController>();
            if (health != null) health.Heal(15);
        }

        public void OnDropItem(GameObject entity)
        {
            Vector3 dropPos = entity.transform.position + entity.transform.forward * 1.5f + Vector3.up * 0.5f;
            CrystalPickup.Drop(0, dropPos, Random.rotation, entity.transform.forward * 4f + Vector3.up * 2f);
        }

        public void OnPickupItem(GameObject entity) { }
    }
}


