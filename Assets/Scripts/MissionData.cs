using UnityEngine;
using System.Collections.Generic;
using Runtime.Scripts.Items;
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
        public List<TradesData> craftingRequirements;

        [Header("Configuración Visual")]
        public GameObject missionPrefab;
    }
}
