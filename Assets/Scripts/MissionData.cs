using UnityEngine;
using System.Collections.Generic;
using Runtime.Scripts.Item;
using Runtime.Scripts.Trades;



namespace Runtime.Scripts.Missions
{
    [CreateAssetMenu(fileName = "Mission_", menuName = "Missions/Mission Data")]
    public class MissionData : ScriptableObject
    {
        [Header("Identificación")]
        public string title;
        [TextArea] public string description;

        [Header("Requisitos de Progresión")]
        public List<MissionData> missionRequirements;

        [Header("Objetivos de Recolección (Inventario)")]
        public List<ItemRequirement> inventoryRequirements;

        [Header("Objetivos de Crafteo (Acción de Receta)")]
        public List<TradeData> craftingRequirements;

        [Header("Configuración Visual")]
        public GameObject missionPrefab;
    }
}
