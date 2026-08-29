using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.InputSystem;

namespace Crafting.Scripts
{
    /// <summary>
    /// Master Crafting Manager: Minecraft-style collaborative UI & Logic.
    /// Hardcoded, no-sprite, centralized in a single hierarchy.
    /// </summary>
    public class CraftingManager : NetworkBehaviour
    {
        public static CraftingManager Instance { get; private set; }

        [Header("Settings")]
        public float slotSize = 80f;
        public float spacing = 8f;

        // The 5x5 Crafting Grid (25 slots). Synced across all clients.
        public NetworkList<FixedString32Bytes> GridItems;
        private FixedString32Bytes[] _offlineGridItems = new FixedString32Bytes[25];

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        private GameObject _canvasRoot;
        private Transform _localInventoryContent;
        private Transform _remoteInventoryContent;
        private Transform _craftingGridRoot;

        private string _pickedItemId = "";
        private TextMeshProUGUI _internalFeedbackText;
        private TextMeshProUGUI _externalFeedbackText;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            GridItems = new NetworkList<FixedString32Bytes>();
        }

        private void Start()
        {
            // Ensure there is an EventSystem for inputs to work
            EnsureEventSystem();

            // Build the UI immediately so it's available
            BuildUI();

            // Initially hide the UI
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && GridItems.Count == 0)
            {
                for (int i = 0; i < 25; i++) GridItems.Add("");
            }

            GridItems.OnListChanged += (changeEvent) => RefreshCraftingVisuals();
            RefreshCraftingVisuals();
        }

        private void Update()
        {
            // Toggle UI with 'C' only using New Input System
            if (Keyboard.current != null)
            {
                if (Keyboard.current.cKey.wasPressedThisFrame)
                {
                    ToggleUI();
                }
            }
        }

        private void SetFeedbackText(string text)
        {
            if (_internalFeedbackText != null) _internalFeedbackText.text = text;
            if (_externalFeedbackText != null) _externalFeedbackText.text = text;
        }

        private TextMeshProUGUI AddText(GameObject go, string content, int size, Color color, TextAlignmentOptions align)
        {
            var txt = go.AddComponent<TextMeshProUGUI>();
            txt.text = content;
            txt.fontSize = size;
            txt.color = color;
            txt.alignment = align;
            txt.raycastTarget = false; // Critical: prevent text from blocking clicks
            return txt;
        }

        private void StretchRT(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
        }

        public void ToggleUI()
        {
            if (_canvasRoot != null)
            {
                bool isActive = !_canvasRoot.activeSelf;
                _canvasRoot.SetActive(isActive);

                // Show/Hide cursor
                Cursor.visible = isActive;
                Cursor.lockState = isActive ? CursorLockMode.None : CursorLockMode.Locked;

                SetFeedbackText(isActive ? "Crafting Menu Open" : "");
            }
        }

        private void BuildUI()
        {
            // 1. Create Canvas
            if (_canvasRoot != null) Destroy(_canvasRoot);

            _canvasRoot = new GameObject("MinecraftCrafting_Canvas");
            // Important: Do NOT set parent to 'transform' to avoid coordinate offsets
            _canvasRoot.transform.SetParent(null);

            Canvas canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // Ensure it's on top

            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _canvasRoot.AddComponent<GraphicRaycaster>();

            // Background Fade
            GameObject bg = CreateUIElement("Background", _canvasRoot.transform);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0, 0, 0, 0.6f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;

            // 2. Main Layout Container (Responsive Area)
            GameObject mainLayout = CreateUIElement("MainLayout", _canvasRoot.transform);
            var mainRt = mainLayout.GetComponent<RectTransform>();
            // Centered vertically but expanded horizontally as requested
            mainRt.anchorMin = new Vector2(0.02f, 0.1f);
            mainRt.anchorMax = new Vector2(0.98f, 0.9f);
            mainRt.sizeDelta = Vector2.zero;

            var hlg = mainLayout.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 20;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; // Don't force stretch, let preferredWidth handle it
            hlg.childForceExpandHeight = true;

            // --- LEFT: INTERNAL INVENTORY ---
            _localInventoryContent = CreateInventoryPanel("Internal Inventory", mainLayout.transform, Color.gray, "LOCAL");

            // --- MIDDLE: CRAFTING 5X5 ---
            _craftingGridRoot = CreateCraftingSection("Crafting Pool", mainLayout.transform);

            // --- RIGHT: EXTERNAL INVENTORY ---
            _remoteInventoryContent = CreateInventoryPanel("External Inventory", mainLayout.transform, new Color(0.4f, 0.2f, 0.2f), "REMOTE");

            PopulateInventory(_localInventoryContent, "Item_Metal", 25, "LOCAL");
            PopulateInventory(_remoteInventoryContent, "Item_Wood", 25, "REMOTE");
        }

        private Transform CreateInventoryPanel(string title, Transform parent, Color bgColor, string tag)
        {
            GameObject panel = CreateUIElement(title, parent);
            panel.AddComponent<LayoutElement>().preferredWidth = 600; // Significantly wider

            Image img = panel.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            panel.AddComponent<Outline>().effectColor = Color.white;

            // Vertical layout for title and scroll
            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(15, 15, 15, 15);
            vlg.spacing = 20;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = vlg.childControlHeight = true;

            // Title
            GameObject label = CreateUIElement("Title", panel.transform);
            label.AddComponent<LayoutElement>().preferredHeight = 50;
            var lTxt = label.AddComponent<TextMeshProUGUI>();
            lTxt.text = title;
            lTxt.fontSize = 28;
            lTxt.alignment = TextAlignmentOptions.Center;
            lTxt.color = Color.white;

            // Scroll View Area
            GameObject scrollGo = CreateUIElement("ScrollView", panel.transform);
            scrollGo.AddComponent<LayoutElement>().flexibleHeight = 1;

            ScrollRect sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;

            GameObject maskGo = CreateUIElement("Mask", scrollGo.transform);
            maskGo.AddComponent<RectMask2D>();
            var maskRt = maskGo.GetComponent<RectTransform>();
            maskRt.anchorMin = Vector2.zero;
            maskRt.anchorMax = Vector2.one;
            maskRt.sizeDelta = Vector2.zero;

            GameObject content = CreateUIElement("Content", maskGo.transform);
            sr.content = content.GetComponent<RectTransform>();
            content.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
            content.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
            content.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1);

            var grid = content.AddComponent<GridLayoutGroup>();
            // Increased cell height to accommodate buttons above each square
            grid.cellSize = new Vector2(slotSize + 20, slotSize + 45);
            grid.spacing = new Vector2(spacing, spacing * 2);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5; // Changed to 5 to match the central grid's 5x5 structure
            grid.childAlignment = TextAnchor.UpperCenter;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Footer Feedback (New position)
            GameObject footer = CreateUIElement("FooterFeedback", panel.transform);
            footer.AddComponent<LayoutElement>().preferredHeight = 40;
            var fTxt = footer.AddComponent<TextMeshProUGUI>();
            fTxt.text = "";
            fTxt.fontSize = 20;
            fTxt.alignment = TextAlignmentOptions.Center;
            fTxt.color = Color.yellow;

            if (tag == "LOCAL") _internalFeedbackText = fTxt;
            else _externalFeedbackText = fTxt;

            return content.transform;
        }

        private Transform CreateCraftingSection(string title, Transform parent)
        {
            GameObject section = CreateUIElement(title, parent);
            section.AddComponent<LayoutElement>().preferredWidth = 750; // Wider for 5x5

            Image img = section.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 0.7f);
            section.AddComponent<Outline>().effectColor = Color.yellow;

            var vlg = section.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 20, 20);
            vlg.spacing = 25;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = vlg.childControlHeight = true;

            // Title Middle
            GameObject label = CreateUIElement("CraftTitle", section.transform);
            label.AddComponent<LayoutElement>().preferredHeight = 40;
            var lTxt = label.AddComponent<TextMeshProUGUI>();
            lTxt.text = title;
            lTxt.fontSize = 32;
            lTxt.color = Color.yellow;
            lTxt.alignment = TextAlignmentOptions.Center;

            GameObject gridHolder = CreateUIElement("GridHolder", section.transform);
            gridHolder.AddComponent<LayoutElement>().preferredHeight = 650;

            var grid = gridHolder.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(slotSize * 1.2f, slotSize * 1.2f + 45); // Scale for 5x5
            grid.spacing = new Vector2(spacing, spacing * 2);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.MiddleCenter;

            for (int i = 0; i < 25; i++)
            {
                int index = i;
                bool isInput = IsInputSlot(i);
                Color slotColor = isInput ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.15f, 0.15f, 0.2f);
                CreateSlotWithControls($"CraftSlot_{index}", gridHolder.transform, slotColor, true, index);
            }

            // --- GENERAL CRAFTING BUTTONS ---
            GameObject btnArea = CreateUIElement("CraftButtons", section.transform);
            btnArea.AddComponent<LayoutElement>().preferredHeight = 70;
            var hlg = btnArea.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 30;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = hlg.childControlHeight = true;

            CreateTextButton("CLEAR GRID", btnArea.transform, new Color(0.6f, 0.1f, 0.1f), () => ClearGrid());
            CreateTextButton("CRAFT ITEM", btnArea.transform, new Color(0.1f, 0.5f, 0.1f), () => TryCraft());

            return gridHolder.transform;
        }

        private void CreateTextButton(string label, Transform parent, Color color, System.Action onClick)
        {
            GameObject btnGo = CreateUIElement(label, parent);
            btnGo.AddComponent<Image>().color = color;
            btnGo.AddComponent<Button>().onClick.AddListener(() => onClick());
            btnGo.AddComponent<Outline>().effectColor = Color.white;

            GameObject txtGo = CreateUIElement("Text", btnGo.transform);
            AddText(txtGo, label, 20, Color.white, TextAlignmentOptions.Center);
            StretchRT(txtGo.GetComponent<RectTransform>());
        }

        private GameObject CreateSlotWithControls(string name, Transform parent, Color color, bool isCrafting, int index, string tag = "")
        {
            GameObject container = CreateUIElement(name + "_Container", parent);
            var vlg = container.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.spacing = 5;

            // 1. Controls Row (Above each slot)
            GameObject controls = CreateUIElement("Controls", container.transform);
            controls.AddComponent<LayoutElement>().preferredHeight = 35;
            var hlg = controls.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4; // Increased spacing
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = hlg.childControlHeight = true;

            // Determine if this is an Output slot in the crafting grid
            bool isOutputSlot = isCrafting && !IsInputSlot(index);

            // Minus Button: Red for Input/Inv, Green for Output (Extract)
            GameObject minusBtn = CreateUIElement("-", controls.transform);
            minusBtn.AddComponent<Image>().color = isOutputSlot ? new Color(0.2f, 0.5f, 0.2f) : new Color(0.5f, 0.2f, 0.2f);
            minusBtn.AddComponent<Button>();
            var mTxtGo = CreateUIElement("T", minusBtn.transform);
            AddText(mTxtGo, "-", 18, Color.white, TextAlignmentOptions.Center);
            StretchRT(mTxtGo.GetComponent<RectTransform>());

            // Qty Text
            GameObject qtyTxt = CreateUIElement("Qty", controls.transform);
            AddText(qtyTxt, "1", 18, Color.white, TextAlignmentOptions.Center);

            // Plus Button: Green for Input/Inv, Red for Output (Re-invest)
            GameObject plusBtn = CreateUIElement("+", controls.transform);
            plusBtn.AddComponent<Image>().color = isOutputSlot ? new Color(0.5f, 0.2f, 0.2f) : new Color(0.2f, 0.5f, 0.2f);
            plusBtn.AddComponent<Button>();
            var pTxtGo = CreateUIElement("T", plusBtn.transform);
            AddText(pTxtGo, "+", 18, Color.white, TextAlignmentOptions.Center);
            StretchRT(pTxtGo.GetComponent<RectTransform>());

            // 2. The Actual Slot
            GameObject slot = CreateUIElement(name, container.transform);
            slot.AddComponent<LayoutElement>().preferredHeight = isCrafting ? slotSize * 1.2f : slotSize;
            Image img = slot.AddComponent<Image>();
            img.color = color;
            slot.AddComponent<Outline>().effectColor = Color.black;

            Button btn = slot.AddComponent<Button>();
            if (isCrafting) btn.onClick.AddListener(() => OnCraftingSlotClicked(index));
            else btn.onClick.AddListener(() => OnInventorySlotClicked(name, tag));

            return container;
        }

        private GameObject CreateUIElement(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private void PopulateInventory(Transform content, string itemId, int count, string tag)
        {
            for (int i = 0; i < count; i++)
            {
                string id = itemId + "_" + i;
                GameObject slotContainer = CreateSlotWithControls(id, content, Color.gray, false, i, tag);

                // Icon (child 1 of container)
                Transform slot = slotContainer.transform.GetChild(1);
                GameObject icon = CreateUIElement("Icon", slot);
                Image img = icon.AddComponent<Image>();
                img.color = (tag == "LOCAL") ? Color.cyan : Color.red;
                var iRt = icon.GetComponent<RectTransform>();
                iRt.anchorMin = iRt.anchorMax = new Vector2(0.5f, 0.5f);
                iRt.sizeDelta = new Vector2(slotSize * 0.7f, slotSize * 0.7f);
            }
        }

        private void ClearGrid()
        {
            for (int i = 0; i < 25; i++) UpdateGrid(i, "");
            SetFeedbackText("Grid Cleared");
        }

        private void TryCraft()
        {
            // Simple Test logic: if there is something in the center, put something in output
            bool found = false;
            for (int i = 0; i < 25; i++)
            {
                if (IsInputSlot(i) && !string.IsNullOrEmpty(IsNetworkActive ? GridItems[i].ToString() : _offlineGridItems[i].ToString()))
                {
                    found = true;
                    break;
                }
            }

            if (found)
            {
                // Put result in slot 0 (an output slot) for testing
                UpdateGrid(0, "Result_Item");
                SetFeedbackText("Crafted! Check outer slots.");
            }
            else
            {
                SetFeedbackText("Nothing to craft.");
            }
        }

        private void OnInventorySlotClicked(string itemId, string tag)
        {
            _pickedItemId = itemId;
            SetFeedbackText($"Picked: {itemId}");
        }

        private void OnCraftingSlotClicked(int index)
        {
            bool isInput = IsInputSlot(index);

            if (string.IsNullOrEmpty(_pickedItemId))
            {
                // Pick item from slot to cursor
                string itemId = IsNetworkActive ? GridItems[index].ToString() : _offlineGridItems[index].ToString();
                if (!string.IsNullOrEmpty(itemId))
                {
                    _pickedItemId = itemId;
                    UpdateGrid(index, "");
                    SetFeedbackText($"Picked: {itemId}");
                }
                return;
            }

            // If we have an item in hand, only allow placing in Input slots
            if (isInput)
            {
                UpdateGrid(index, _pickedItemId);
                _pickedItemId = "";
                SetFeedbackText("Item placed. Pick another.");
            }
            else
            {
                SetFeedbackText("Cannot place items in Output slots!");
            }
        }

        private void UpdateGrid(int index, string itemId)
        {
            if (index < 0 || index >= 25) return;

            if (IsNetworkActive)
            {
                UpdateGridServerRpc(index, itemId, NetworkManager.Singleton.LocalClientId);
            }
            else
            {
                _offlineGridItems[index] = itemId;
                RefreshCraftingVisuals();
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void UpdateGridServerRpc(int index, string itemId, ulong clientId)
        {
            GridItems[index] = itemId;
        }

        private void RefreshCraftingVisuals()
        {
            if (_craftingGridRoot == null) return;

            for (int i = 0; i < 25; i++)
            {
                // Slot is now the second child of the container
                Transform container = _craftingGridRoot.GetChild(i);
                Transform slot = container.GetChild(1);
                string itemId = IsNetworkActive ? GridItems[i].ToString() : _offlineGridItems[i].ToString();
                bool isInput = IsInputSlot(i);

                // Visual refresh
                Image img = slot.GetComponent<Image>();
                if (string.IsNullOrEmpty(itemId))
                {
                    img.color = isInput ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.15f, 0.15f, 0.2f);
                }
                else
                {
                    img.color = isInput ? Color.yellow : new Color(0.2f, 0.8f, 0.2f); // Input yellow, Output Green
                }

                // Clear old icons if any
                foreach (Transform child in slot) if(child.name == "ItemLabel") Destroy(child.gameObject);

                if (!string.IsNullOrEmpty(itemId))
                {
                    GameObject label = CreateUIElement("ItemLabel", slot);
                    AddText(label, itemId.Split('_')[0], 14, Color.black, TextAlignmentOptions.Center);
                    StretchRT(label.GetComponent<RectTransform>());
                }
            }
        }

        private bool IsInputSlot(int index)
        {
            int row = index / 5;
            int col = index % 5;
            // Inner 3x3 is between (1,1) and (3,3) in a 5x5 grid
            return row >= 1 && row <= 3 && col >= 1 && col <= 3;
        }
    }
}
