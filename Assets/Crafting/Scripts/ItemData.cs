using UnityEngine;
using UnityEngine.Serialization;

namespace Crafting.Scripts
{
    [System.Serializable]
    public struct ItemRequirement
    {
        public ItemData item;
        public int amount;

        public ItemRequirement(ItemData item, int amount = 1)
        {
            this.item = item;
            this.amount = amount <= 0 ? 1 : amount;
        }
    }

    [System.Serializable]
    public class ItemAmount
    {
        public ItemData item;

        [Header("Cantidad")]
        public int amount = 1;

        [Header("Rango Aleatorio (Opcional)")]
        public bool useRange = false;
        public int minAmount = 1;
        public int maxAmount = 1;

        [Header("Probabilidad de Recompensa (0% - 100%)")]
        [Range(0f, 100f)]
        public float dropChance = 100f;

        public ItemAmount() { }

        public ItemAmount(ItemData item, int amount = 1)
        {
            this.item = item;
            this.amount = amount;
            this.useRange = false;
            this.minAmount = amount;
            this.maxAmount = amount;
            this.dropChance = 100f;
        }

        public int GetAmount()
        {
            if (dropChance < 100f && Random.value * 100f > dropChance)
            {
                return 0;
            }

            if (useRange)
            {
                int min = Mathf.Min(minAmount, maxAmount);
                int max = Mathf.Max(minAmount, maxAmount);
                return Random.Range(min, max + 1);
            }

            return amount;
        }

        public int GetMinRequiredAmount()
        {
            if (useRange)
            {
                return Mathf.Min(minAmount, maxAmount);
            }
            return amount;
        }
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
        void OnDropItem(GameObject player);
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
        public int defaultAmount = 1;

        // Hash estable entre PCs
        public override int GetHashCode()
        {
            int h = 17;
            foreach (char c in (itemName ?? "").ToLowerInvariant()) h = unchecked(h * 31 + c);
            return h;
        }

        [Header("Representación")]
        [FormerlySerializedAs("icon")]
        public Sprite itemSprite;

        [FormerlySerializedAs("worldPrefab")]
        public GameObject itemPrefab;
    }
}
