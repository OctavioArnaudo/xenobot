using UnityEngine;
using NGO.Gameplay.Base;
using Unity.Netcode;

namespace NGO.Gameplay.Networking
{
    /// <summary>
    /// Script de fin de herencia para Salud.
    /// </summary>
    public class NetworkHealthNetworking : HealthBase
    {
        public override void ModifyHealthRpc(int amount)
        {
            if (!IsServer) return;

            health.Value = Mathf.Clamp(health.Value + amount, 0, 100);
            if (health.Value <= 0)
            {
                Debug.Log($"{gameObject.name} ha muerto.");
            }
        }
    }
}
