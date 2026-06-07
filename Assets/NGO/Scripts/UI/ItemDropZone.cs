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
        public class OnItemDroppedEvent : UnityEvent<ItemData, int> { }

        public OnItemDroppedEvent onItemDropped;

        public void OnDrop(PointerEventData eventData)
        {
            if (eventData.pointerDrag != null)
            {
                DraggableItem draggable = eventData.pointerDrag.GetComponent<DraggableItem>();
                if (draggable != null)
                {
                    Debug.Log($"[DropZone] Objeto detectado: {draggable.itemData.ItemName}");
                    onItemDropped.Invoke(draggable.itemData, draggable.amount);

                    // Opcional: Devolver el objeto a su origen o destruirlo si el market lo "consume" visualmente
                    // Por ahora el DraggableItem volverá a su sitio en OnEndDrag si no cambiamos su padre.
                }
            }
        }
    }
}
