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

        // The 3x3 Crafting Grid (9 slots). Synced across all clients.
        public NetworkList<FixedString32Bytes> GridItems;
        private FixedString32Bytes[] _offlineGridItems = new FixedString32Bytes[9];

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        private GameObject _canvasRoot;
        private Transform _localInventoryContent;
        private Transform _remoteInventoryContent;
        private Transform _craftingGridRoot;

        private string _pickedItemId = "";
        private TextMeshProUGUI _feedbackText;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            GridItems = new NetworkList<FixedString32Bytes>();
        }

        private void Start()
        {
            // Build the UI immediately so it's available
            BuildUI();

            // Initially hide the UI
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && GridItems.Count == 0)
            {
                for (int i = 0; i < 9; i++) GridItems.Add("");
            }

            GridItems.OnListChanged += (changeEvent) => RefreshCraftingVisuals();
            RefreshCraftingVisuals();
        }

        private void Update()
        {
            // Toggle UI with 'E' or 'C' using New Input System
            if (Keyboard.current != null)
            {
                if (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.cKey.wasPressedThisFrame)
                {
                    ToggleUI();
                }
            }
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

                _feedbackText.text = isActive ? "Crafting Menu Open" : "";
            }
        }

        private void BuildUI()
        {
            // 1. Create Canvas
            _canvasRoot = new GameObject("MinecraftCrafting_Canvas");
            _canvasRoot.transform.SetParent(transform);
            Canvas canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
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
            // Use anchors to maintain margins regardless of screen size
            mainRt.anchorMin = new Vector2(0.05f, 0.1f);
            mainRt.anchorMax = new Vector2(0.95f, 0.9f);
            mainRt.sizeDelta = Vector2.zero;

            var hlg = mainLayout.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 20;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = hlg.childForceExpandHeight = true;

            // --- LEFT: LOCAL PLAYER INVENTORY ---
            _localInventoryContent = CreateInventoryPanel("My Inventory", mainLayout.transform, Color.gray, "LOCAL");

            // --- MIDDLE: CRAFTING 3X3 ---
            _craftingGridRoot = CreateCraftingSection("Crafting Pool", mainLayout.transform);

            // --- RIGHT: REMOTE PLAYER INVENTORY ---
            _remoteInventoryContent = CreateInventoryPanel("Player 2 Inventory", mainLayout.transform, new Color(0.4f, 0.2f, 0.2f), "REMOTE");

            // Feedback Text
            GameObject feedbackGo = CreateUIElement("Feedback", _canvasRoot.transform);
            _feedbackText = feedbackGo.AddComponent<TextMeshProUGUI>();
            _feedbackText.text = "Click to pick an item";
            _feedbackText.alignment = TextAlignmentOptions.Center;
            _feedbackText.fontSize = 24;
            var fRt = feedbackGo.GetComponent<RectTransform>();
            fRt.anchorMin = new Vector2(0.5f, 0);
            fRt.anchorMax = new Vector2(0.5f, 0);
            fRt.anchoredPosition = new Vector2(0, 50);

            PopulateInventory(_localInventoryContent, "Item_Metal", 30, "LOCAL");
            PopulateInventory(_remoteInventoryContent, "Item_Wood", 15, "REMOTE");
        }

        private Transform CreateInventoryPanel(string title, Transform parent, Color bgColor, string tag)
        {
            GameObject panel = CreateUIElement(title, parent);
            panel.AddComponent<LayoutElement>().preferredWidth = 400; // Increased width

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
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperCenter;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return content.transform;
        }

        private Transform CreateCraftingSection(string title, Transform parent)
        {
            GameObject section = CreateUIElement(title, parent);
            section.AddComponent<LayoutElement>().preferredWidth = 500;

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
            gridHolder.AddComponent<LayoutElement>().preferredHeight = 550; // More space for controls

            var grid = gridHolder.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(slotSize * 1.5f, slotSize * 1.5f + 45);
            grid.spacing = new Vector2(spacing * 2, spacing * 3);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.MiddleCenter;

            for (int i = 0; i < 9; i++)
            {
                int index = i;
                CreateSlotWithControls($"CraftSlot_{index}", gridHolder.transform, new Color(0.3f, 0.3f, 0.3f), true, index);
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
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 20;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;

            var rt = txtGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
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
            hlg.spacing = 2;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = hlg.childControlHeight = true;

            // Minus Button
            GameObject minusBtn = CreateUIElement("-", controls.transform);
            minusBtn.AddComponent<Image>().color = new Color(0.5f, 0.2f, 0.2f);
            minusBtn.AddComponent<Button>();
            CreateUIElement("T", minusBtn.transform).AddComponent<TextMeshProUGUI>().text = "-";
            minusBtn.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;

            // Qty Text
            GameObject qtyTxt = CreateUIElement("Qty", controls.transform);
            var qT = qtyTxt.AddComponent<TextMeshProUGUI>();
            qT.text = "1"; qT.alignment = TextAlignmentOptions.Center; qT.fontSize = 18; qT.color = Color.white;

            // Plus Button
            GameObject plusBtn = CreateUIElement("+", controls.transform);
            plusBtn.AddComponent<Image>().color = new Color(0.2f, 0.5f, 0.2f);
            plusBtn.AddComponent<Button>();
            CreateUIElement("T", plusBtn.transform).AddComponent<TextMeshProUGUI>().text = "+";
            plusBtn.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;

            // 2. The Actual Slot
            GameObject slot = CreateUIElement(name, container.transform);
            slot.AddComponent<LayoutElement>().preferredHeight = isCrafting ? slotSize * 1.5f : slotSize;
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
            for (int i = 0; i < 9; i++) UpdateGrid(i, "");
            _feedbackText.text = "Grid Cleared";
        }

        private void TryCraft()
        {
            _feedbackText.text = "Crafting... (Logic Pending)";
        }

        private void OnInventorySlotClicked(string itemId, string tag)
        {
            _pickedItemId = itemId;
            _feedbackText.text = $"Picked: {itemId}";
        }

        private void OnCraftingSlotClicked(int index)
        {
            if (string.IsNullOrEmpty(_pickedItemId))
            {
                // If picking from grid to clear
                UpdateGrid(index, "");
                return;
            }

            UpdateGrid(index, _pickedItemId);
            _pickedItemId = ""; // Reset pick
            _feedbackText.text = "Item placed. Pick another.";
        }

        private void UpdateGrid(int index, string itemId)
        {
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

            for (int i = 0; i < 9; i++)
            {
                // Slot is now the second child of the container
                Transform container = _craftingGridRoot.GetChild(i);
                Transform slot = container.GetChild(1);
                string itemId = IsNetworkActive ? GridItems[i].ToString() : _offlineGridItems[i].ToString();

                // Simple visual refresh: if slot has item, make it brighter or add a text label
                Image img = slot.GetComponent<Image>();
                img.color = string.IsNullOrEmpty(itemId) ? new Color(0.3f, 0.3f, 0.3f) : Color.yellow;

                // Clear old icons if any
                foreach (Transform child in slot) if(child.name == "ItemLabel") Destroy(child.gameObject);

                if (!string.IsNullOrEmpty(itemId))
                {
                    GameObject label = CreateUIElement("ItemLabel", slot);
                    var txt = label.AddComponent<TextMeshProUGUI>();
                    txt.text = itemId.Split('_')[0]; // Show base name
                    txt.fontSize = 14;
                    txt.color = Color.black;
                    txt.alignment = TextAlignmentOptions.Center;

                    var rt = label.GetComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.sizeDelta = Vector2.zero;
                }
            }
        }
    }
}