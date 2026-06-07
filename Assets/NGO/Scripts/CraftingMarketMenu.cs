using UnityEngine;
using NGO.Gameplay.Base;
using NGO.Data;
using Unity.Netcode;

namespace NGO.Networking
{
    /// <summary>
    /// Manager para el Canvas CraftingMarketMenu.
    /// Intercambia objetos según reglas definidas en ScriptableObjects.
    /// </summary>
    public class CraftingMarketMenu : MonoBehaviour
    {
        [Header("Referencias de Red")]
        [SerializeField] private TradeBase tradeService;

        [Header("Configuración de Trades")]
        [SerializeField] private TradeRecipe[] possibleTrades;

        [Header("UI - Feedback")]
        [SerializeField] private UnityEngine.UI.Image dropPreviewIcon;

        /// <summary>
        /// Método llamado por ItemDropZone al arrastrar un objeto.
        /// Intenta encontrar una receta que use este objeto.
        /// </summary>
        public void OnItemDroppedInZone(ItemData item, int amount)
        {
            if (item == null) return;

            if (dropPreviewIcon != null)
            {
                dropPreviewIcon.sprite = item.Icon;
                dropPreviewIcon.gameObject.SetActive(true);
            }

            // Buscar la primera receta que use este objeto como entrada
            for (int i = 0; i < possibleTrades.Length; i++)
            {
                if (possibleTrades[i].InputItem.ItemID == item.ItemID)
                {
                    Debug.Log($"[CraftingMenu] Receta encontrada para {item.ItemName}. Iniciando intercambio...");
                    OnClickTrade(i);
                    return;
                }
            }

            Debug.LogWarning($"[CraftingMenu] No se encontró ninguna receta que use {item.ItemName}");
        }

        /// <summary>
        /// Ejecuta un intercambio (Dropear materiales y recoger producto).
        /// </summary>
        public void OnClickTrade(int recipeIndex)
        {
            if (tradeService == null)
            {
                Debug.LogError("[CraftingMenu] tradeService no está asignado en el Inspector.");
                return;
            }

            if (possibleTrades == null || recipeIndex < 0 || recipeIndex >= possibleTrades.Length)
            {
                Debug.LogError($"[CraftingMenu] Índice de receta {recipeIndex} fuera de rango o lista vacía.");
                return;
            }

            TradeRecipe selectedRecipe = possibleTrades[recipeIndex];
            if (selectedRecipe == null)
            {
                Debug.LogError($"[CraftingMenu] La receta en el índice {recipeIndex} es NULL.");
                return;
            }

            // 1. Ejecución Local (Predictiva / Offline)
            ulong myId = (NetworkManager.Singleton != null) ? NetworkManager.Singleton.LocalClientId : 0;
            tradeService.ExecuteTradeLocal(recipeIndex, myId);
            Debug.Log($"[CraftingMenu] Ejecución local completada: {selectedRecipe.OutputItem.ItemName}");

            // 2. Sincronización de Red (Paso final)
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
            {
                tradeService.RequestTradeRpc(recipeIndex, myId);
                Debug.Log("[CraftingMenu] Sincronización de red enviada al servidor.");
            }
            else
            {
                Debug.LogWarning("[CraftingMenu] Trabajando en modo OFFLINE. La red no está conectada.");
            }
        }
    }
}
