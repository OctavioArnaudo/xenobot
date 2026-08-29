using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Events;
using NGO.Data;

namespace NGO.UI
{
    public class DraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Datos del Objeto")]
        public ItemData itemData;
        public int amount = 1;
        public int sourceSlotIndex = -1; // -1: Inventario, 1-9: Rejilla

        [Header("Referencias Visuales")]
        [SerializeField] private Image iconImage;

        public UnityAction<DraggableItem> OnBeginDragAction;
        public UnityAction<DraggableItem, bool> OnEndDragAction;

        private Canvas _canvas;
        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Vector2 _originalPosition;
        private Transform _originalParent;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();

            // Asegurar que existe el CanvasGroup
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            _canvas = GetComponentInParent<Canvas>();

            if (iconImage != null && itemData != null)
                iconImage.sprite = itemData.icon;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (itemData == null) return;

            OnBeginDragAction?.Invoke(this);

            _originalPosition = _rectTransform.anchoredPosition;
            _originalParent = transform.parent;

            transform.SetParent(_canvas.transform, true);
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 0.6f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (itemData == null) return;
            _rectTransform.anchoredPosition += eventData.delta / _canvas.scaleFactor;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (itemData == null) return;

            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 1.0f;

            bool success = transform.parent != _canvas.transform;
            OnEndDragAction?.Invoke(this, success);

            if (!success)
            {
                transform.SetParent(_originalParent, true);
                _rectTransform.anchoredPosition = _originalPosition;
            }
        }
    }
}
