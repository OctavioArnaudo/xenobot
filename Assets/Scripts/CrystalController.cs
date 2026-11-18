using UnityEngine;
using Unity.Netcode;
using Runtime.Scripts.Items;
using Runtime.Scripts.Pickup;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Effective;

namespace Runtime.Scripts.Crystals
{
    [RequireComponent(typeof(NetworkObject))]
    public class CrystalController : NetworkBehaviour, IItemPickupAction
    {
        private const float DEFAULT_SCALE = 2.0f;
        public Optional<float> renderScale;
        public float EffectiveRenderScale => renderScale.Get(2.0f, DEFAULT_SCALE);

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

        public void OnPickupItem(GameObject entity) { }
    }
}

