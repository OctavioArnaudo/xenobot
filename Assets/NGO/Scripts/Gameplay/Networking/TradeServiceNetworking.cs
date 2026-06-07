using UnityEngine;
using NGO.Gameplay.Base;
using Unity.Netcode;
using NGO.Data;

namespace NGO.Gameplay.Networking
{
    public class TradeServiceNetworking : TradeBase
    {
        [SerializeField] private TradeRecipe[] availableRecipes;

        public override void ExecuteTradeLocal(int recipeId, ulong clientId)
        {
            if (recipeId < 0 || recipeId >= availableRecipes.Length) return;

            TradeRecipe recipe = availableRecipes[recipeId];
            Debug.Log($"[Trade Logic] Procesando: {recipe.InputItem.ItemName} -> {recipe.OutputItem.ItemName} para el cliente {clientId}");

            // Aquí iría la lógica de quitar items del inventario local y añadir el nuevo
        }

        public override void RequestTradeRpc(int recipeId, ulong clientId)
        {
            if (!IsServer) return;

            Debug.Log($"[Server] Recibida petición de tradeo del cliente {clientId}");
            ExecuteTradeLocal(recipeId, clientId);
        }
    }
}
