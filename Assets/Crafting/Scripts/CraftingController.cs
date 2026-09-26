using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using System.Collections.Generic;
using Trades.Data;
using System.Linq;
using Crafting.Scripts;
using Combating.Scripts;

namespace Crafting.Scripts
{
    /// <summary>
    /// Unified controller for the Crafting System.
    /// Handles UI (Minimized & Expanded), Trigger proximity, and Debug activation.
    /// </summary>
    public class CraftingController : NetworkBehaviour
    {
        public static CraftingController Instance { get; private set; }

        public bool IsUIOpen => _open;

        [Header("System Settings")]
        public List<TradeData> availableTrades;
        public bool requireProximity = true;

        [Header("UI Aesthetics")]
        public int panelWidth = 500;
        public int panelHeight = 550;
        public int titleH = 65;
        public int padding = 20;
        public int cornerRadius = 15;
        public Color panelColor = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        public Color accentColor = new Color(1f, 0.85f, 0f, 1f);

        [Header("Minimized UI Settings")]
        public int minWidth = 250;
        public int minHeight = 40;

        private bool _open;
        private bool _isPlayerInRange;
        private Vector2 _scrollPos;
        private int _selectedRecipeIndex = -1;

        private Texture2D _texPanel, _texSlot, _texSelected, _texBtnNormal, _texBtnHover;
        private GUIStyle _titleSty, _recipeSty, _btnSty, _infoSty, _qtySty, _minSty;
        private bool _stylesReady;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private void Awake()
        {
            if (Instance == null) Instance = this;

            if (GetComponent<Collider>() == null)
            {
                var col = gameObject.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(5, 5, 5);
            }
        }

        private void Update()
        {
            if (Keyboard.current == null) return;

            // ACTIVACIÓN DEBUG (Tecla T): En cualquier momento
            if (Keyboard.current.tKey.wasPressedThisFrame)
            {
                SetOpen(!_open);
            }

            // ACTIVACIÓN NORMAL (Tecla C): Solo si está en rango
            if (Keyboard.current.cKey.wasPressedThisFrame)
            {
                if (!requireProximity || _isPlayerInRange)
                {
                    SetOpen(!_open);
                }
            }
        }

        public void SetOpen(bool open)
        {
            _open = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
            if (!open) _selectedRecipeIndex = -1;
        }

        #region Trigger Proximity
        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                _isPlayerInRange = true;
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                _isPlayerInRange = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                _isPlayerInRange = false;
                if (_open) SetOpen(false);
            }
        }
        #endregion

        #region UI Rendering
        private void OnGUI()
        {
            // Solo dibujamos si estamos en rango O si la UI está abierta (Debug T)
            if (!_isPlayerInRange && !_open) return;

            EnsureStyles();

            if (_open)
            {
                DrawExpandedUI();
            }
            else if (_isPlayerInRange)
            {
                DrawMinimizedUI();
            }
        }

        private void DrawMinimizedUI()
        {
            float x = (Screen.width - minWidth) / 2f;
            float y = Screen.height - minHeight - 20;
            Rect rect = new Rect(x, y, minWidth, minHeight);

            GUI.DrawTexture(rect, _texPanel);
            CreateOutline(rect, accentColor);
            GUI.Label(rect, "PRESIONA [C] PARA CRAFTEAR", _minSty);
        }

        private void DrawExpandedUI()
        {
            float screenW = Screen.width;
            float screenH = Screen.height;

            var allInvs = Object.FindObjectsByType<InventoryController>(FindObjectsSortMode.None);
            var myInv = InventoryController.LocalInstance;
            var otherInvs = allInvs.Where(x => x != myInv).ToList();

            float sideW = 400;
            float centerW = panelWidth;
            float totalW = sideW * 2 + centerW + 40;
            float xStart = (screenW - totalW) / 2f;
            float y0 = (screenH - panelHeight) / 2f;

            if (myInv != null)
            {
                myInv.DrawInventoryUI(new Rect(xStart, y0, sideW, panelHeight), "MI INVENTARIO");
            }

            Rect centerRect = new Rect(xStart + sideW + 20, y0, centerW, panelHeight);
            DrawCraftingPanel(centerRect);

            if (otherInvs.Count > 0)
            {
                otherInvs[0].DrawInventoryUI(new Rect(xStart + sideW + centerW + 40, y0, sideW, panelHeight), "INVENTARIO COMPAÑERO");
            }
            else
            {
                GUI.DrawTexture(new Rect(xStart + sideW + centerW + 40, y0, sideW, panelHeight), _texPanel);
                GUI.Label(new Rect(xStart + sideW + centerW + 40, y0, sideW + padding, panelHeight), "ESPERANDO A OTRO JUGADOR...", _infoSty);
            }

            if (GUI.Button(new Rect(screenW / 2 + totalW / 2 - 50, y0 + 15, 35, 35), "X", _btnSty)) SetOpen(false);
        }

        private void DrawCraftingPanel(Rect rect)
        {
            GUI.DrawTexture(rect, _texPanel);
            GUI.Label(new Rect(rect.x, rect.y + 10, rect.width, titleH), "ESTACIÓN DE TRABAJO", _titleSty);

            float paddingInner = 20;
            Rect listRect = new Rect(rect.x + paddingInner, rect.y + titleH + 10, rect.width * 0.45f, rect.height - titleH - 30);
            Rect detailRect = new Rect(rect.x + rect.width * 0.5f, rect.y + titleH + 10, rect.width * 0.45f, rect.height - titleH - 30);

            GUI.BeginGroup(listRect);
            _scrollPos = GUI.BeginScrollView(new Rect(0, 0, listRect.width, listRect.height), _scrollPos, new Rect(0, 0, listRect.width - 20, availableTrades.Count * 55));
            for (int i = 0; i < availableTrades.Count; i++)
            {
                Rect r = new Rect(0, i * 55, listRect.width - 20, 50);
                bool isSelected = (_selectedRecipeIndex == i);
                GUI.DrawTexture(r, isSelected ? _texSelected : _texSlot);
                if (availableTrades[i].OutputItem != null)
                {
                    if (availableTrades[i].OutputItem.itemSprite != null)
                        GUI.DrawTexture(new Rect(5, i * 55 + 5, 40, 40), availableTrades[i].OutputItem.itemSprite.texture);
                    GUI.Label(new Rect(50, i * 55, listRect.width - 60, 50), availableTrades[i].OutputItem.itemName, _recipeSty);
                }
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    _selectedRecipeIndex = i;
                    Event.current.Use();
                }
            }
            GUI.EndScrollView();
            GUI.EndGroup();

            if (_selectedRecipeIndex >= 0)
            {
                TradeData recipe = availableTrades[_selectedRecipeIndex];
                GUI.BeginGroup(detailRect);
                float y = 0;
                GUI.Label(new Rect(0, y, detailRect.width, 25), "REQUIERE:", _infoSty); y += 30;
                GUI.DrawTexture(new Rect(0, y, 60, 60), _texSlot);
                if (recipe.InputItem.itemSprite != null) GUI.DrawTexture(new Rect(5, y + 5, 50, 50), recipe.InputItem.itemSprite.texture);
                GUI.Label(new Rect(0, y, 60, 60), "x" + recipe.InputAmount, _qtySty);
                GUI.Label(new Rect(70, y + 15, detailRect.width - 70, 30), recipe.InputItem.itemName, _recipeSty);
                y += 75;
                GUI.Label(new Rect(detailRect.width / 2 - 15, y - 5, 30, 30), "↓", _titleSty); y += 30;
                GUI.Label(new Rect(0, y, detailRect.width, 25), "OBTIENES:", _infoSty); y += 30;
                GUI.DrawTexture(new Rect(0, y, 60, 60), _texSlot);
                if (recipe.OutputItem.itemSprite != null) GUI.DrawTexture(new Rect(5, y + 5, 50, 50), recipe.OutputItem.itemSprite.texture);
                GUI.Label(new Rect(0, y, 60, 60), "x" + recipe.OutputAmount, _qtySty);
                GUI.Label(new Rect(70, y + 15, detailRect.width - 70, 30), recipe.OutputItem.itemName, _recipeSty);
                y += 85;
                Rect btnR = new Rect(0, y, detailRect.width, 50);
                GUI.DrawTexture(btnR, btnR.Contains(Event.current.mousePosition) ? _texBtnHover : _texBtnNormal);
                if (GUI.Button(btnR, "CRAFTEAR", _btnSty)) TryExecuteTrade(_selectedRecipeIndex);
                GUI.EndGroup();
            }
        }
        #endregion

        #region Trade Logic
        private void TryExecuteTrade(int index)
        {
            if (index < 0 || index >= availableTrades.Count) return;

            TradeData recipe = availableTrades[index];
            ulong myId = (NetworkManager.Singleton != null) ? NetworkManager.Singleton.LocalClientId : 0;

            if (CanCraft(recipe))
            {
                var allInvs = Object.FindObjectsByType<InventoryController>(FindObjectsSortMode.None);
                var otherInv = allInvs.FirstOrDefault(x => x != InventoryController.LocalInstance);
                ulong targetId = (otherInv != null) ? otherInv.OwnerClientId : myId;

                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
                    RequestTradeServerRpc(index, myId, targetId);
                else
                    ExecuteTradeLocal(index, myId, targetId);
            }
            else
            {
                Debug.LogWarning("[Crafting] Materiales insuficientes para " + recipe.OutputItem.itemName);
            }
        }

        private bool CanCraft(TradeData recipe)
        {
            var bag = InventoryController.GetBag();
            string key = recipe.InputItem.itemName.ToLowerInvariant();
            if (bag.TryGetValue(key, out var slot)) return slot.qty >= recipe.InputAmount;
            return false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestTradeServerRpc(int recipeId, ulong clientId, ulong targetId) => ExecuteTradeLocal(recipeId, clientId, targetId);

        public void ExecuteTradeLocal(int recipeId, ulong clientId, ulong targetId)
        {
            if (recipeId < 0 || recipeId >= availableTrades.Count) return;
            TradeData recipe = availableTrades[recipeId];

            var allInvs = Object.FindObjectsByType<InventoryController>(FindObjectsSortMode.None);
            var crafterInv = allInvs.FirstOrDefault(x => x.OwnerClientId == clientId);
            var receiverInv = allInvs.FirstOrDefault(x => x.OwnerClientId == targetId);

            if (crafterInv == null) crafterInv = InventoryController.LocalInstance;
            if (receiverInv == null) receiverInv = crafterInv;

            if (crafterInv != null)
            {
                if (IsNetworkActive) crafterInv.RemoveItemServerRpc(recipe.InputItem.GetItemHashCode(), recipe.InputAmount);
                else crafterInv.InternalAddItem(recipe.InputItem.GetItemHashCode(), -recipe.InputAmount);
            }

            if (receiverInv != null)
            {
                if (IsNetworkActive) receiverInv.AddItemServerRpc(recipe.OutputItem.GetItemHashCode(), recipe.OutputAmount);
                else receiverInv.InternalAddItem(recipe.OutputItem.GetItemHashCode(), recipe.OutputAmount);
            }

            InventoryController.MarkCountDirty();
        }
        #endregion

        #region Helpers
        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _texPanel = MakeRoundedTex(64, cornerRadius, panelColor, Color.clear, 0);
            _texSlot = MakeRoundedTex(64, 8, new Color(1f, 1f, 1f, 0.08f), Color.clear, 0);
            _texSelected = MakeRoundedTex(64, 8, new Color(1f, 1f, 1f, 0.15f), accentColor, 2);
            _texBtnNormal = MakeRoundedTex(64, 10, new Color(0.2f, 0.2f, 0.25f, 1f), Color.white, 1);
            _texBtnHover = MakeRoundedTex(64, 10, new Color(0.3f, 0.3f, 0.4f, 1f), accentColor, 2);
            _titleSty = Sty(32, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            _recipeSty = Sty(18, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
            _btnSty = Sty(20, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            _infoSty = Sty(16, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.8f));
            _qtySty = Sty(14, FontStyle.Bold, TextAnchor.LowerRight, accentColor);

            _minSty = new GUIStyle(_infoSty) {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 18
            };
            _minSty.normal.textColor = Color.white;

            _stylesReady = true;
        }

        private void CreateOutline(Rect r, Color c)
        {
            float t = 2f;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private Texture2D MakeRoundedTex(int s, int r, Color fill, Color border, int bw)
        {
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            Color clear = new Color(0, 0, 0, 0);
            Color[] px = new Color[s * s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float cx = Mathf.Clamp(x, r, s - 1 - r), cy = Mathf.Clamp(y, r, s - 1 - r);
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > r + 1f) px[y * s + x] = clear;
                    else if (d > r - 0.5f) px[y * s + x] = Color.Lerp(fill, clear, d - (r - 0.5f));
                    else if (bw > 0 && d > r - bw) px[y * s + x] = border;
                    else px[y * s + x] = fill;
                }
            }
            tex.SetPixels(px); tex.Apply(); return tex;
        }

        private static GUIStyle Sty(int sz, FontStyle fs, TextAnchor a, Color c)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = sz, fontStyle = fs, alignment = a };
            s.normal.textColor = c;
            return s;
        }
        #endregion
    }
}
