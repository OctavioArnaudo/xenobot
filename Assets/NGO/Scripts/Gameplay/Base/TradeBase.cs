using Unity.Netcode;
using UnityEngine;

namespace NGO.Gameplay.Base
{
    /// <summary>
    /// Estructura base para el mercado de crafteo/intercambio fijo.
    /// </summary>
    public abstract class TradeBase : NetworkBehaviour
    {
        /// <summary>
        /// Procesa el intercambio de forma local. Útil para previsualización o modo offline.
        /// </summary>
        public abstract void ExecuteTradeLocal(int recipeId, ulong clientId);

        [Rpc(SendTo.Server)]
        public virtual void RequestTradeRpc(int recipeId, ulong clientId) { }
    }
}
