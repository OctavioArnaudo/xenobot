using UnityEngine;
using UnityEngine.InputSystem;
using Dialogs.Scripts;

namespace Dialogs.Scripts
{
    [AddComponentMenu("Dialogs/Dialog Trigger")]
    public class DialogTrigger : MonoBehaviour
    {
        public enum ActivationMode { OnEnter, OnInputInsideTrigger }

        [Header("Configuración")]
        public string dialogId;
        public ActivationMode activationMode = ActivationMode.OnEnter;
        public bool onlyOnce = true;

        [Header("Prompt Visual (Solo modo Input)")]
        public bool showPrompt = true;
        public string promptText = "[E] Hablar";

        private bool _used = false;
        private bool _playerInside = false;

        private GameObject _promptGO;
        private TMPro.TextMeshProUGUI _promptTMP;

        private void OnTriggerEnter(Collider other)
        {
            if (_used && onlyOnce) return;
            if (!other.CompareTag("Player")) return;

            if (activationMode == ActivationMode.OnEnter)
            {
                TryOpenDialog();
            }
            else
            {
                _playerInside = true;
                if (showPrompt) ShowPrompt();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInside = false;
            HidePrompt();
        }

        private void Update()
        {
            if (activationMode != ActivationMode.OnInputInsideTrigger) return;
            if (!_playerInside) return;
            if (_used && onlyOnce) return;

            // Si ya hay un diálogo abierto, esa E le pertenece al DialogManager (avanzar/cerrar), no a nosotros
            if (DialogManager.Instance != null && DialogManager.Instance.IsOpen) return;

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                TryOpenDialog();
            }
        }

        private void TryOpenDialog()
        {
            if (DialogManager.Instance == null)
            {
                Debug.LogWarning("[DialogTrigger] No se encontró DialogManager en la escena.");
                return;
            }

            HidePrompt();
            DialogManager.Instance.ShowDialog(dialogId);
            _used = true;
        }

        // --- Prompt hardcoded simple (opcional) ---

        private void ShowPrompt()
        {
            if (!showPrompt || (_used && onlyOnce)) return;
            if (DialogManager.Instance != null && DialogManager.Instance.IsOpen) return;

            if (_promptGO == null)
            {
                GameObject canvasObj = GameObject.Find("DialogPromptCanvas");
                if (canvasObj == null)
                {
                    canvasObj = new GameObject("DialogPromptCanvas");
                    var c = canvasObj.AddComponent<Canvas>();
                    c.renderMode = RenderMode.ScreenSpaceOverlay;
                    c.sortingOrder = 150;
                    canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
                    canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                }

                _promptGO = new GameObject("Prompt_" + dialogId);
                _promptGO.transform.SetParent(canvasObj.transform, false);
                _promptTMP = _promptGO.AddComponent<TMPro.TextMeshProUGUI>();
                _promptTMP.text = promptText;
                _promptTMP.fontSize = 22;
                _promptTMP.color = Color.white;
                _promptTMP.alignment = TMPro.TextAlignmentOptions.Center;

                RectTransform rt = _promptGO.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.35f);
                rt.sizeDelta = new Vector2(300, 50);
            }

            _promptGO.SetActive(true);
        }

        private void HidePrompt()
        {
            if (_promptGO != null) _promptGO.SetActive(false);
        }
    }
}