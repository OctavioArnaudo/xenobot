using System;
using System.Reflection;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;

namespace Combating.Scripts
{
    public static class CombatDamage
    {
        public static bool TryApply(GameObject hitObject, float damage, GameObject source)
        {
            if (hitObject == null)
                return false;

            Damageable fpsDamageable = hitObject.GetComponentInParent<Damageable>();
            if (fpsDamageable != null)
            {
                fpsDamageable.InflictDamage(damage, false, source);
                return true;
            }

            CombatDamageReceiver receiver = hitObject.GetComponentInParent<CombatDamageReceiver>();
            if (receiver != null)
            {
                receiver.TakeDamage(damage, source);
                return true;
            }

            MonoBehaviour[] behaviours = hitObject.GetComponentsInParent<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                Type type = behaviour.GetType();
                MethodInfo takeDamage = type.GetMethod("TakeDamage", new[] { typeof(int) });
                if (takeDamage != null)
                {
                    takeDamage.Invoke(behaviour, new object[] { Mathf.RoundToInt(damage) });
                    return true;
                }

                MethodInfo applyDamageRpc = type.GetMethod("ApplyDamageRpc", new[] { typeof(int) });
                if (applyDamageRpc != null && CanInvokeRpc(behaviour))
                {
                    applyDamageRpc.Invoke(behaviour, new object[] { Mathf.RoundToInt(damage) });
                    return true;
                }
            }

            return false;
        }

        static bool CanInvokeRpc(MonoBehaviour behaviour)
        {
            NetworkBehaviour networkBehaviour = behaviour as NetworkBehaviour;
            return networkBehaviour != null &&
                   NetworkManager.Singleton != null &&
                   NetworkManager.Singleton.IsListening &&
                   networkBehaviour.IsSpawned;
        }

        public static bool AreFriendly(GameObject a, GameObject b)
        {
            if (a == null || b == null)
                return false;

            CombatTeamMember teamA = a.GetComponentInParent<CombatTeamMember>();
            CombatTeamMember teamB = b.GetComponentInParent<CombatTeamMember>();
            return teamA != null && teamB != null && teamA.Team != CombatTeam.Neutral && teamA.Team == teamB.Team;
        }
    }
}
