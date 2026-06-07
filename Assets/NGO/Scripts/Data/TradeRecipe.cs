using UnityEngine;

namespace NGO.Data
{
    [CreateAssetMenu(fileName = "NewTradeRecipe", menuName = "NGO/Inventory/Trade Recipe")]
    public class TradeRecipe : ScriptableObject
    {
        public ItemData InputItem;
        public int InputAmount;
        public ItemData OutputItem;
        public int OutputAmount;
    }
}
