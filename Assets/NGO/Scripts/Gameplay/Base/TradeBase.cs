using Unity.Netcode;
using UnityEngine;

namespace NGO.Gameplay.Base
{
    /// <summary>
    /// Estructura base para el mercado de crafteo/intercambio fijo.
    /// </summary>
    public abstract class TradeBase : NetworkBehaviour
    {
        [Rpc(SendTo.Server)]
        public virtual void RequestTradeRpc(int recipeId, ulong clientId) { }
    }
}
