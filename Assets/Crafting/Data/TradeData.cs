using UnityEngine;

namespace NGO.Data
{
    [CreateAssetMenu(fileName = "NewTradeData", menuName = "NGO/Inventory/Trade Data")]
    public class TradeData : ScriptableObject
    {
        public ItemData InputItem;
        public int InputAmount;
        public ItemData OutputItem;
        public int OutputAmount;
    }
}
