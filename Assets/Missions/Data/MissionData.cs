using UnityEngine;
using System.Collections.Generic;

namespace Missions.Data
{
    /// <summary>
    /// Definición de los datos de la misión.
    /// Este archivo debe llamarse MissionData.cs y estar en una carpeta de Scripts o Data.
    /// </summary>
    [CreateAssetMenu(fileName = "Mission_Data", menuName = "Missions/Mission Data")]
    public class MissionData : ScriptableObject
    {
        public string missionId;
        public string title;
        [TextArea] public string description;
        public string requiredLocation;
        public List<string> requiredMissionIds;

        [Tooltip("Asigna aquí el prefab específico de la misión si es necesario")]
        public GameObject missionPrefab;
    }
}
