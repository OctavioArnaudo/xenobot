using UnityEngine;

namespace Xenobot.ModularCombat
{
    public class PrefabSetupNotes : MonoBehaviour
    {
        [TextArea(4, 10)]
        public string Notes =
            "Player: add CombatTeamMember=Player and ClickToShoot. Assign AimCamera and Muzzle if needed.\n" +
            "Enemy: add CombatTeamMember=Enemy, CombatDamageReceiver and optionally BasicAttackAI.\n" +
            "ProjectilePrefab should point to ModularProjectile.prefab. ImpactVfxPrefab points to ModularImpactVfx.prefab.";
    }
}
