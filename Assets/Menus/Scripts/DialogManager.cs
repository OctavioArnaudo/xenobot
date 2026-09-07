using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace Dialogs.Scripts
{
    [System.Serializable]
    public class DialogEntry
    {
        public string id;
        public string speaker;
        [TextArea(2, 5)] public string[] lines;
    }

    public class DialogManager : MonoBehaviour
    {
        public static DialogManager Instance { get; private set; }

        [Header("Diálogos (editar acá)")]
        [SerializeField] private List<DialogEntry> dialogs = new List<DialogEntry>();

        private GameObject _panel;
        private TextMeshProUGUI _speakerTMP;
        private TextMeshProUGUI _bodyTMP;

        private DialogEntry _current;
        private int _lineIndex;
        private int _openedOnFrame = -1;

        public bool IsOpen => _panel != null && _panel.activeSelf;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            BuildUI();
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Time.frameCount == _openedOnFrame) return;

            if (Keyboard.current != null &&
                (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame))
            {
                Advance();
            }
        }

        public void ShowDialog(string id)
        {
            DialogEntry entry = dialogs.Find(d => d.id == id);
            if (entry == null || entry.lines == null || entry.lines.Length == 0)
            {
                Debug.LogWarning($"[DialogManager] Diálogo '{id}' no encontrado o vacío.");
                return;
            }

            _current = entry;
            _lineIndex = 0;
            _openedOnFrame = Time.frameCount;
            OpenPanel();
            RenderLine();
        }

        private void Advance()
        {
            _lineIndex++;
            if (_current == null || _lineIndex >= _current.lines.Length)
            {
                ClosePanel();
                return;
            }
            RenderLine();
        }

        private void RenderLine()
        {
            _speakerTMP.text = string.IsNullOrEmpty(_current.speaker) ? "" : _current.speaker;
            _bodyTMP.text = _current.lines[_lineIndex];
        }

        private void OpenPanel()
        {
            _panel.SetActive(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void ClosePanel()
        {
            _panel.SetActive(false);
            _current = null;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        // --- UI Hardcoded (panel chico, centrado) ---

        private void BuildUI()
        {
            GameObject canvasObj = new GameObject("DialogCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("DialogPanel");
            _panel.transform.SetParent(canvasObj.transform, false);
            _panel.AddComponent<Image>().color = new Color(0, 0, 0, 0.88f);

            RectTransform rt = _panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(520, 160);

            _speakerTMP = CreateText("Speaker", 20, Color.yellow, new Vector2(0, 1), new Vector2(1, 1), TextAlignmentOptions.Center, new Vector2(15, -12), new Vector2(-15, 34));
            _speakerTMP.fontStyle = FontStyles.Bold;

            _bodyTMP = CreateText("Body", 18, Color.white, Vector2.zero, Vector2.one, TextAlignmentOptions.Center, new Vector2(15, 12), new Vector2(-15, -40));

            CreateText("Hint", 13, new Color(1, 1, 1, 0.55f), new Vector2(0.5f, 0), new Vector2(0.5f, 0), TextAlignmentOptions.Center, new Vector2(-70, 6), new Vector2(70, 26)).text = "[E] Continuar";

            _panel.SetActive(false);
        }

        private TextMeshProUGUI CreateText(string name, int size, Color color, Vector2 anchorMin, Vector2 anchorMax, TextAlignmentOptions align, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_panel.transform, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return tmp;
        }
    }
}