using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using NGO.Data;

namespace NGO.UI
{
    /// <summary>
    /// Componente para hacer que un icono de objeto sea arrastrable en la UI.
    /// </summary>
    public class DraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Datos del Objeto")]
        public ItemData itemData;
        public int amount = 1;

        [Header("Referencias Visuales")]
        [SerializeField] private Image iconImage;

        private Canvas _canvas;
        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Vector2 _originalPosition;
        private Transform _originalParent;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvas = GetComponentInParent<Canvas>();

            if (iconImage != null && itemData != null)
                iconImage.sprite = itemData.Icon;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _originalPosition = _rectTransform.anchoredPosition;
            _originalParent = transform.parent;

            // Mover al frente de todo durante el arrastre
            transform.SetParent(_canvas.transform, true);
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 0.6f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Seguir el ratón escalado al canvas
            _rectTransform.anchoredPosition += eventData.delta / _canvas.scaleFactor;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 1.0f;

            // Si no fue soltado en un DropZone válido, vuelve a su sitio
            if (transform.parent == _canvas.transform)
            {
                transform.SetParent(_originalParent, true);
                _rectTransform.anchoredPosition = _originalPosition;
            }
        }
    }
}
