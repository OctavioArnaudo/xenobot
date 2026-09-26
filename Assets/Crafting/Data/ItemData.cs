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

    [CreateAssetMenu(menuName = "Items/Item Data", fileName = "Item_")]
    public class ItemData : ScriptableObject
    {
        [Header("Identificación")]
        public string itemName;

        [Header("Categorización")]

        [Tooltip("¿Se puede recoger del suelo o solo se puede usar/dropear/quit?")]
        public bool isPickable = true;

        [Tooltip("¿Afecta/aplica un efecto sobre el propio jugador al usarse desde el inventario?")]
        public bool isUsable = false;

        [Tooltip("¿Se puede quitar del inventario o solo se puede usar/dropear?")]
        public bool isQuitable = true;

        [Tooltip("¿Se puede tirar al suelo desde el inventario o solo se puede usar/quit?")]
        public bool isDropable = true;

        public int maxStack = 99;

        [Header("Representación")]
        [FormerlySerializedAs("icon")]
        public Sprite itemSprite;

        [FormerlySerializedAs("worldPrefab")]
        public GameObject itemPrefab;

        public int GetItemHashCode()
        {
            if (string.IsNullOrEmpty(itemName)) return 0;
            return itemName.ToLowerInvariant().GetHashCode();
        }
    }
}
