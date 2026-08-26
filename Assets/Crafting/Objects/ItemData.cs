using UnityEngine;

namespace NGO.Data
{
    [CreateAssetMenu(fileName = "NewItem", menuName = "NGO/Inventory/Item")]
    public class ItemData : ScriptableObject
    {
        public int ItemID;
        public string ItemName;
        public Sprite Icon;
    }
}
