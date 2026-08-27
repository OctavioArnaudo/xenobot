using UnityEngine;

namespace Xenobot.ModularCombat
{
    public enum CombatTeam
    {
        Neutral,
        Player,
        Enemy,
    }

    public class CombatTeamMember : MonoBehaviour
    {
        public CombatTeam Team = CombatTeam.Neutral;
    }
}
