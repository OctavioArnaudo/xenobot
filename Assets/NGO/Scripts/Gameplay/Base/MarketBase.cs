using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

namespace NGO.Gameplay.Base
{
    /// <summary>
    /// Estructura base para el mercado de intercambio entre jugadores.
    /// </summary>
    public abstract class MarketBase : NetworkBehaviour
    {
        // En un sistema real, usaríamos una NetworkList de estructuras personalizadas.
        // Aquí definimos los métodos RPC para interactuar con el mercado global.

        [Rpc(SendTo.Server)]
        public virtual void OfferItemRpc(int itemId, int quantity, ulong senderId) { }

        [Rpc(SendTo.Server)]
        public virtual void ClaimItemRpc(int offerIndex, ulong claimerId) { }
    }
}
