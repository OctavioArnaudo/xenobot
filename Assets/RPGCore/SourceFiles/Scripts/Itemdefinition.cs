using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/Item Definition", fileName = "Item_New")]
public class ItemDefinition : ScriptableObject
{
    [Header("Identificación")]
    public string itemId;
    public string displayName;

    [Header("Visual")]
    public Sprite icon;

    [Header("Comportamiento")]
    public ItemType type;
    public bool isUsable;
    public bool isStackable = true;
    public int maxStack = 99;

    [TextArea]
    public string description;

    [Header("EXP")]
    [Tooltip("Si > 0 este ítem otorga EXP al recogerse (Exp A, Exp B, etc.)")]
    public float expValue = 0f;
}

public enum ItemType
{
    Collectible,
    Key,
    Consumable,
    Equipment,
    Currency,
    ExpOrb,     // nuevo — objetos de experiencia
}