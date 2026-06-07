using UnityEngine;
using NGO.Gameplay.Base;
using Unity.Netcode;
using NGO.Data;

namespace NGO.Gameplay.Networking
{
    public class TradeServiceNetworking : TradeBase
    {
        [SerializeField] private TradeRecipe[] availableRecipes;

        public override void RequestTradeRpc(int recipeId, ulong clientId)
        {
            if (!IsServer) return;

            // Validación de receta y materiales
            Debug.Log($"[Trade] Cliente {clientId} solicita trade con receta {recipeId}");

            // Simulación de intercambio exitoso
            Debug.Log("[Trade] Intercambio procesado en el servidor.");
        }
    }
}
