using UnityEngine;
using System.Collections.Generic;

namespace Runtime.Scripts.Levels
{
    [CreateAssetMenu(fileName = "Level_", menuName = "Levels/Level Data")]
    public class LevelsData : ScriptableObject
    {
        [Header("Level Configuration")]
        public string levelName;
        public Sprite thumbnail;
        public string sceneName;

        [Header("Statistics (Current Session)")]
        public string bestTime = "00:00";
        public float bestTimeSeconds = float.MaxValue;
        public bool bestIsVictory = false;
        public List<string> completedPlayers = new List<string>();

        public void UpdateRecord(string formattedTime, float timeSeconds, bool isVictory, string player = "")
        {
            bool update = false;

            if (bestTimeSeconds == float.MaxValue || bestTime == "00:00")
            {
                update = true;
            }
            else if (isVictory && !bestIsVictory)
            {
                update = true;
            }
            else if (isVictory == bestIsVictory)
            {
                if (timeSeconds < bestTimeSeconds)
                {
                    update = true;
                }
            }

            if (update)
            {
                bestTime = formattedTime;
                bestTimeSeconds = timeSeconds;
                bestIsVictory = isVictory;
            }

            if (isVictory && !string.IsNullOrEmpty(player))
            {
                if (!completedPlayers.Contains(player))
                {
                    completedPlayers.Add(player);
                }
            }
        }
    }
}