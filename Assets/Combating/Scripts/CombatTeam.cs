using UnityEngine;

namespace Combating.Scripts
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
