using UnityEngine;
using Crafting.Scripts;

namespace Trades.Data
{
    [CreateAssetMenu(menuName = "Trades/Trade Data", fileName = "Trade_")]
    public class TradeData : ScriptableObject
    {
        public ItemData InputItem;
        public int InputAmount;
        public ItemData OutputItem;
        public int OutputAmount;
    }
}