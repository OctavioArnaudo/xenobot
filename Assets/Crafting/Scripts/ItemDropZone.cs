using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using NGO.Data;

namespace NGO.UI
{
    /// <summary>
    /// Zona que detecta cuando se suelta un DraggableItem sobre ella.
    /// </summary>
    public class ItemDropZone : MonoBehaviour, IDropHandler
    {
        [System.Serializable]
        public class OnItemDroppedEvent : UnityEvent<ItemData, int, int> { }

        public int slotIndex = 0; // El ID de este slot (0-8)
        public OnItemDroppedEvent onItemDropped = new OnItemDroppedEvent();

        public void OnDrop(PointerEventData eventData)
        {
            if (eventData.pointerDrag != null)
            {
                DraggableItem draggable = eventData.pointerDrag.GetComponent<DraggableItem>();
                if (draggable != null && draggable.itemData != null)
                {
                    Debug.Log($"[DropZone] Objeto detectado: {draggable.itemData.ItemName} en slot {slotIndex}");
                    onItemDropped?.Invoke(draggable.itemData, draggable.amount, slotIndex);
                }
            }
        }
    }
}
