using UnityEngine;
using NGO.Gameplay.Base;
using NGO.Data;
using NGO.UI;
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

        [Header("UI - Grid 3x3")]
        [Tooltip("Asigna las 9 imágenes de los slots. Estas imágenes actuarán como fondo y como icono a la vez.")]
        [SerializeField] private UnityEngine.UI.Image[] gridImages = new UnityEngine.UI.Image[9];

        private DraggableItem[] m_GridDraggables = new DraggableItem[9];
        private int[] m_CurrentGridIds = new int[9];

        private void Awake()
        {
            // Inicializar la rejilla de 9 slots
            for (int i = 0; i < 9; i++)
            {
                m_CurrentGridIds[i] = -1;

                if (gridImages[i] != null)
                {
                    gridImages[i].color = new Color(0, 0, 0, 0);
                    gridImages[i].sprite = null;

                    // Configurar el componente DraggableItem en cada slot de la rejilla para permitir reubicación
                    m_GridDraggables[i] = gridImages[i].GetComponent<DraggableItem>();
                    if (m_GridDraggables[i] == null)
                        m_GridDraggables[i] = gridImages[i].gameObject.AddComponent<DraggableItem>();

                    m_GridDraggables[i].sourceSlotIndex = i + 1; // 1-9
                    m_GridDraggables[i].OnBeginDragAction = OnGridItemStartDrag;
                }
            }
        }

        private void OnGridItemStartDrag(DraggableItem item)
        {
            int index = item.sourceSlotIndex - 1;
            if (index >= 0 && index < 9)
            {
                Debug.Log($"[CraftingMenu] Levantando objeto del slot {item.sourceSlotIndex}.");

                // Vaciamos el ID en la lógica para que el hueco se considere libre
                m_CurrentGridIds[index] = -1;

                // Mantenemos los datos en el DraggableItem que estamos moviendo,
                // pero vaciamos la imagen visual del slot de origen
                if (gridImages[index] != null)
                {
                    gridImages[index].color = new Color(0, 0, 0, 0);
                    // No quitamos el sprite todavía para que el "fantasma" que arrastramos sea visible
                }
            }
        }

        private void ClearSlot(int index)
        {
            m_CurrentGridIds[index] = -1;
            if (gridImages[index] != null)
            {
                gridImages[index].color = new Color(0, 0, 0, 0);
                gridImages[index].sprite = null;
            }
            if (m_GridDraggables[index] != null)
            {
                m_GridDraggables[index].itemData = null;
            }
        }

        /// <summary>
        /// Maneja el drop de objetos.
        /// Slot 0 = Padre (Autoubicación)
        /// Slots 1-9 = Cuadrantes específicos
        /// </summary>
        public void OnItemDroppedInSlot(ItemData item, int amount, int slotIndex)
        {
            if (item == null) return;

            int targetIndex = -1;

            if (slotIndex == 0)
            {
                // Buscamos el primer hueco vacío en los cuadrantes (índices 0-8 internos, que son slots 1-9 UI)
                for (int i = 0; i < 9; i++)
                {
                    if (m_CurrentGridIds[i] == -1)
                    {
                        targetIndex = i;
                        break;
                    }
                }

                if (targetIndex == -1)
                {
                    Debug.LogWarning("[CraftingMenu] La rejilla está llena. No se puede autoubicación.");
                    return;
                }
            }
            else
            {
                // El usuario soltó en un cuadrante específico (1-9)
                targetIndex = slotIndex - 1;
            }

            if (targetIndex >= 0 && targetIndex < 9)
            {
                UpdateSlot(targetIndex, item);
            }
        }

        private void UpdateSlot(int index, ItemData item)
        {
            m_CurrentGridIds[index] = item.itemId;

            if (gridImages[index] != null)
            {
                gridImages[index].color = Color.white;
                gridImages[index].sprite = item.icon;
            }

            if (m_GridDraggables[index] != null)
            {
                m_GridDraggables[index].itemData = item;
            }

            Debug.Log($"[CraftingMenu] Cuadrante {index + 1} actualizado con {item.displayName}");
            CheckForValidRecipe();
        }

        private void CheckForValidRecipe()
        {
            // Lógica para detectar recetas basadas en la posición de la rejilla
        }

        public void OnClickTrade(int recipeIndex)
        {
            if (tradeService == null)
            {
                Debug.LogError("[CraftingMenu] tradeService no está asignado en el Inspector.");
                return;
            }

            if (possibleTrades == null || recipeIndex < 0 || recipeIndex >= possibleTrades.Length)
            {
                Debug.LogError($"[CraftingMenu] Índice de receta {recipeIndex} fuera de rango.");
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
            Debug.Log($"[CraftingMenu] Ejecución local completada: {selectedRecipe.OutputItem.displayName}");

            // 2. Sincronización de Red (Paso final)
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
            {
                tradeService.RequestTradeRpc(recipeIndex, myId);
                Debug.Log("[CraftingMenu] Sincronización de red enviada al servidor.");
            }
            else
            {
                Debug.LogWarning("[CraftingMenu] Modo OFFLINE.");
            }
        }
    }
}
