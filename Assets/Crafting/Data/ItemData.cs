using UnityEngine;
using UnityEngine.Serialization;

namespace Crafting.Scripts
{
    public interface IItemFunctional
    {
        void ApplyEffect(GameObject player);
    }

    /// <summary>
    /// Acción ejecutada cuando el ítem se usa desde el inventario (Botón USE).
    /// </summary>
    public interface IItemUseAction
    {
        void OnUseItem(GameObject player);
    }

    /// <summary>
    /// Acción ejecutada cuando el ítem se tira al suelo desde el inventario (Botón DROP).
    /// </summary>
    public interface IItemDropAction
    {
        void OnDropItem(GameObject player, GameObject droppedInstance);
    }

    /// <summary>
    /// Acción ejecutada cuando un equipo se desequipa o quita (Botón QUIT).
    /// </summary>
    public interface IItemQuitAction
    {
        void OnQuitItem(GameObject player);
    }

    /// <summary>
    /// Acción ejecutada al recoger el ítem del suelo (PickupController).
    /// </summary>
    public interface IItemPickupAction
    {
        void OnPickupItem(GameObject player);
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
