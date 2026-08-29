using UnityEngine;
using TMPro;
using NGO.Gameplay.Base;
using Unity.Netcode;

namespace NGO.Networking
{
    /// <summary>
    /// Manager para el Canvas ExchangeMarketMenu.
    /// Maneja transferencias y reclamos entre usuarios.
    /// </summary>
    public class ExchangeMarketMenu : MonoBehaviour
    {
        [Header("Referencias de Red")]
        [SerializeField] private MarketBase marketService;

        [Header("UI - Oferta")]
        [SerializeField] private TMP_InputField itemIdInput;
        [SerializeField] private TMP_InputField amountInput;
        [SerializeField] private UnityEngine.UI.Image selectedItemIcon;

        [Header("UI - Listas")]
        [SerializeField] private Transform contentContainer;
        [SerializeField] private GameObject entryPrefab;

        private int _currentSelectedItemId = -1;

        /// <summary>
        /// Método llamado por el ItemDropZone cuando se arrastra un icono aquí.
        /// </summary>
        public void OnItemDroppedInZone(ItemData item, int amount)
        {
            if (item == null) return;

            _currentSelectedItemId = item.itemId;
            if (itemIdInput != null) itemIdInput.text = item.itemId.ToString();
            if (amountInput != null) amountInput.text = amount.ToString();
            if (selectedItemIcon != null)
            {
                selectedItemIcon.sprite = item.icon;
                selectedItemIcon.gameObject.SetActive(true);
            }

            Debug.Log($"[ExchangeMenu] Objeto seleccionado mediante arrastre: {item.displayName}");
        }

        /// <summary>
        /// Envía un objeto al mercado (Drop/Transfer).
        /// </summary>
        public void OnClickTransfer()
        {
            if (marketService == null) return;

            int id = _currentSelectedItemId;

            // Si no hay ID por arrastre, intentar leer del input
            if (id == -1 && !string.IsNullOrEmpty(itemIdInput.text))
                id = int.Parse(itemIdInput.text);

            if (id == -1) return;

            int qty = int.Parse(amountInput.text);
            ulong myId = NetworkManager.Singleton.LocalClientId;

            marketService.OfferItemRpc(id, qty, myId);
            Debug.Log($"[ExchangeMenu] Transfiriendo {qty} de {id} al mercado.");

            // Limpiar selección
            ResetSelection();
        }

        private void ResetSelection()
        {
            _currentSelectedItemId = -1;
            if (selectedItemIcon != null) selectedItemIcon.gameObject.SetActive(false);
            if (itemIdInput != null) itemIdInput.text = "";
        }

        /// <summary>
        /// Reclama un objeto del mercado (Catch/Claim).
        /// </summary>
        /// <param name="index">Índice de la oferta en la lista.</param>
        public void OnClickClaim(int index)
        {
            if (marketService == null) return;

            ulong myId = NetworkManager.Singleton.LocalClientId;
            marketService.ClaimItemRpc(index, myId);
            Debug.Log($"[ExchangeMenu] Reclamando oferta {index}.");
        }
    }
}
