using UnityEngine;
using UnityEngine.Serialization;

namespace Crafting.Scripts
{
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
        Resource,   // Items solo stackeables (ej: Iron)
        Consumable, // Uso manual en inventario sobre sí mismo / compartible (ej: Life, Fuel, Ammo)
        Equipment,  // Efecto en inventario sobre sí mismo, con/sin renderizado (ej: Weapon, Shield)
        Usability,  // No sobre sí mismo; afecta únicamente al entorno (ej: Key)
        Additivity, // Efecto sobre sí mismo únicamente al momento de pickearlo (ej: Experience, Dialogs)
        Wearable    // Afecta únicamente al renderizado/apariencia (ej: Mask, Costumes)
    }

    [CreateAssetMenu(menuName = "Items/Item Data", fileName = "Item_")]
    public class ItemData : ScriptableObject
    {
        [Header("Identificación")]
        public string itemCode;
        public string itemName;

        [Header("Categorización")]
        public ItemType type = ItemType.Resource;

        [Tooltip("¿El ítem afecta o crea un renderizado/mesh en el mundo o en el jugador?")]
        public bool isEquippable = true;

        [Tooltip("¿Afecta/aplica un efecto sobre el propio jugador al usarse desde el inventario?")]
        public bool isUsable = false;

        [Tooltip("¿Se puede apilar en varias unidades en la misma casilla del inventario?")]
        public bool isStackable = true;

        [Tooltip("¿Se puede tirar al suelo desde el inventario o solo se puede usar/quit?")]
        public bool isDroppable = true;

        [Tooltip("¿Se puede quitar del inventario o solo se puede usar/dropear?")]
        public bool isQuitable = true;

        [Tooltip("¿Se puede recoger del suelo o solo se puede usar/dropear/quit?")]
        public bool isPickupable = true;

        public int maxStack = 99;

        [Header("Representación")]
        [FormerlySerializedAs("icon")]
        public Sprite itemSprite;

        [TextArea]
        public string itemDescription;

        [FormerlySerializedAs("worldPrefab")]
        public GameObject itemPrefab;

        public int GetItemHashCode()
        {
            if (string.IsNullOrEmpty(itemCode)) return 0;
            return itemCode.ToLowerInvariant().GetHashCode();
        }
    }
}
