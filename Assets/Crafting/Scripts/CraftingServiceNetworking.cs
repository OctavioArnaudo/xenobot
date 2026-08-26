using UnityEngine;
using NGO.Gameplay.Base;
using Unity.Netcode;

namespace NGO.Gameplay.Networking
{
    /// <summary>
    /// Script de fin de herencia para Crafting.
    /// </summary>
    public class CraftingServiceNetworking : CraftingBase
    {
        public override void RequestCraftRpc(int itemIDToCraft, RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            Debug.Log($"[Crafting] Cliente {clientId} solicita fabricar {itemIDToCraft}");

            // Lógica de validación de materiales (vacío de prueba)
            bool canCraft = true;
            if (canCraft)
            {
                Debug.Log("Crafting exitoso.");
            }
        }
    }
}
