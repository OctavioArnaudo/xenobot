using UnityEngine;

/// <summary>
/// ScriptableObject que define un tipo de ítem.
/// Crear en: Assets → click derecho → Create → Inventory → Item Definition
/// </summary>
[CreateAssetMenu(menuName = "Inventory/Item Definition", fileName = "Item_New")]
public class ItemDefinition : ScriptableObject
{
    [Header("Identificación")]
    public string itemId;           // ID único, ej: "star", "key_red", "potion_hp"
    public string displayName;      // Nombre visible en UI

    [Header("Visual")]
    public Sprite icon;

    [Header("Comportamiento")]
    public ItemType type;
    public bool isUsable;           // Si aparece botón "Usar" al seleccionarlo
    public bool isStackable = true; // Si acumula cantidad o es única
    public int maxStack = 99;

    [TextArea]
    public string description;
}

public enum ItemType
{
    Collectible,    // estrellas — solo se recolectan
    Key,            // llaves — desbloquean algo
    Consumable,     // potions — se usan y desaparecen
    Equipment,      // se equipa/desequipa
    Currency,       // dinero — stackable, acumula sin límite visual
}