using UnityEngine;
using System.Collections.Generic;
using Runtime.Scripts.Items;

namespace Runtime.Scripts.Trades
{
    [CreateAssetMenu(menuName = "Trades/Trade Data", fileName = "Trade_")]
    public class TradesData : ScriptableObject
    {
        [Header("Insumos / Entradas (Muchos a Muchos)")]
        public List<ItemAmount> inputs = new List<ItemAmount>();

        [Header("Productos / Salidas (Muchos a Muchos)")]
        public List<ItemAmount> outputs = new List<ItemAmount>();

        // Propiedades de compatibilidad con llamadas legadas
        public ItemsData InputItem => inputs != null && inputs.Count > 0 ? inputs[0].item : null;
        public int InputAmount => inputs != null && inputs.Count > 0 ? inputs[0].amount : 0;
        public ItemsData OutputItem => outputs != null && outputs.Count > 0 ? outputs[0].item : null;
        public int OutputAmount => outputs != null && outputs.Count > 0 ? outputs[0].amount : 0;
    }
}
