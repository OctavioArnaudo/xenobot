using UnityEngine;
using System.Collections.Generic;
using Crafting.Scripts;

namespace Trades.Data
{
    [CreateAssetMenu(menuName = "Trades/Trade Data", fileName = "Trade_")]
    public class TradeData : ScriptableObject
    {
        [Header("Insumos / Entradas (Muchos a Muchos)")]
        public List<ItemAmount> inputs = new List<ItemAmount>();

        [Header("Productos / Salidas (Muchos a Muchos)")]
        public List<ItemAmount> outputs = new List<ItemAmount>();

        // Propiedades de compatibilidad con llamadas legadas
        public ItemData InputItem => inputs != null && inputs.Count > 0 ? inputs[0].item : null;
        public int InputAmount => inputs != null && inputs.Count > 0 ? inputs[0].amount : 0;
        public ItemData OutputItem => outputs != null && outputs.Count > 0 ? outputs[0].item : null;
        public int OutputAmount => outputs != null && outputs.Count > 0 ? outputs[0].amount : 0;
    }
}
