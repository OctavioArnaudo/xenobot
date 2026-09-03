using UnityEngine;
using UnityEngine.Serialization;

namespace Crafting.Scripts
{
    public interface IItemFunctional
    {
        void ApplyEffect(GameObject player);
    }

    public enum ItemType
    {
        Resource,
        Consumable,
        Equipment,
        Experience,
        KeyItem
    }

    [CreateAssetMenu(menuName = "Items/Item Data", fileName = "Item_")]
    public class ItemData : ScriptableObject
    {
        [Header("Identificación")]
        public string itemCode;
        public string displayName;

        [Header("Visual")]
        [FormerlySerializedAs("icon")]
        public Sprite itemSprite;

        [Header("Comportamiento")]
        public ItemType type;

        [Tooltip("¿Se puede apilar en el inventario?")]
        public bool isStackable = true;
        public int maxStack = 99;

        [Tooltip("¿El jugador puede usarlo manualmente desde el inventario?")]
        public bool canUse;

        [Tooltip("¿Se usa automáticamente al recogerlo del suelo?")]
        public bool autoUse;

        [TextArea]
        public string description;

        [Header("World Representation")]
        [FormerlySerializedAs("worldPrefab")]
        public GameObject itemPrefab;

        public int GetItemHashCode()
        {
            if (string.IsNullOrEmpty(itemCode)) return 0;
            return itemCode.ToLowerInvariant().GetHashCode();
        }
    }
}
