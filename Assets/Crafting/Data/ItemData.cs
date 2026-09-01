using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Inventory/Item Data", fileName = "Item_New")]
public class ItemData : ScriptableObject
{
    [FormerlySerializedAs("itemId")]
    [SerializeField]
    private int _itemId;

    public int itemId
    {
        get => _itemId;
        set => _itemId = value;
    }

    [Header("Identificación")]
    public string itemCode;
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

    [Header("World Representation")]
    public GameObject worldPrefab;

    private void OnValidate()
    {
        if (_itemId == 0)
        {
            // Generar un ID único basado en el hash del nombre del asset si no tiene uno
            _itemId = Mathf.Abs(name.GetHashCode());
            if (_itemId == 0) _itemId = Random.Range(1, 999999);

            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            #endif
        }
    }
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