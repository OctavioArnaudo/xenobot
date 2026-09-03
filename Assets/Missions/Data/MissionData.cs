using UnityEngine;
using System.Collections.Generic;
using Crafting.Scripts;

namespace Missions.Data
{
    /// <summary>
    /// Definición de los datos de la misión.
    /// Este archivo debe llamarse MissionData.cs y estar en una carpeta de Scripts o Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Mission_Data", menuName = "Missions/Mission Data")]
    public class MissionData : ScriptableObject
    {
        public enum MissionMode { Singleplayer, Multiplayer, Hybrid }

        [System.Serializable]
        public struct ItemRequirement
        {
            public ItemData item;
            public int amount;
        }

        [Header("Identificación")]
        public string missionId;
        public string title;
        [TextArea] public string description;
        public MissionMode mode;
        public bool isFinalMission;

        [Header("Requisitos de Progresión")]
        public string requiredLocation;
        public List<string> requiredMissionIds;

        [Header("Objetivos de Recolección")]
        public List<ItemRequirement> gatheringRequirements;

        [Header("Objetivos de Crafteo")]
        public List<ItemRequirement> craftingRequirements;

        [Header("Objetivos de Habilidades")]
        public List<string> requiredSkillIds;

        [Header("Configuración Visual")]
        [Tooltip("Asigna aquí el prefab específico de la misión si es necesario")]
        public GameObject missionPrefab;
    }
}
