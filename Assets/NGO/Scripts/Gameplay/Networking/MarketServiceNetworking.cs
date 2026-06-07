using UnityEngine;
using NGO.Gameplay.Base;
using Unity.Netcode;
using System.Collections.Generic;

namespace NGO.Gameplay.Networking
{
    public class MarketServiceNetworking : MarketBase
    {
        // Ejemplo simplificado de almacenamiento en el servidor
        private List<string> m_MarketLog = new List<string>();

        public override void OfferItemRpc(int itemId, int quantity, ulong senderId)
        {
            if (!IsServer) return;

            string entry = $"Player {senderId} offered {quantity} of Item {itemId}";
            m_MarketLog.Add(entry);
            Debug.Log($"[Market] {entry}");

            // Aquí se restaría del inventario del jugador
        }

        public override void ClaimItemRpc(int offerIndex, ulong claimerId)
        {
            if (!IsServer) return;

            Debug.Log($"[Market] Player {claimerId} claimed item at index {offerIndex}");
            // Aquí se añadiría al inventario del reclamante
        }
    }
}
