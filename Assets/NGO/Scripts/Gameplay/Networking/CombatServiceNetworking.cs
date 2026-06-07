using UnityEngine;
using NGO.Gameplay.Base;
using Unity.Netcode;

namespace NGO.Gameplay.Networking
{
    /// <summary>
    /// Script de fin de herencia para Combate.
    /// Aplica la lógica de juego sobre la estructura de red base.
    /// </summary>
    public class CombatServiceNetworking : CombatBase
    {
        public override void ExecuteActionRpc(int type, Vector3 origin, ulong instigatorId)
        {
            // Lógica de combate (ejemplo vacío para evitar errores)
            Debug.Log($"[Combat] Ejecutando acción tipo {type} en {origin} por {instigatorId}");

            float radius = 5f;
            Collider[] hits = Physics.OverlapSphere(origin, radius);
            foreach (var hit in hits)
            {
                if (hit.TryGetComponent<HealthBase>(out var health))
                {
                    health.ModifyHealthRpc(-20);
                }
            }
        }
    }
}
