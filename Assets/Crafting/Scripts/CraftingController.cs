using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Collections;
using System.Collections.Generic;
using Trades.Data;
using System.Linq;
using Crafting.Scripts;
using Combating.Scripts;
using Missions.Scripts;

namespace Crafting.Scripts
{
    /// <summary>
    /// Unified controller for the Crafting System.
    /// Handles UI, trigger proximity, player-attached crafting, and tracking completed trades.
    /// </summary>
    public class CraftingController : NetworkBehaviour
    {
        public static CraftingController Instance { get; private set; }
        public static CraftingController LocalInstance { get; private set; }

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

        // Registro de trades crafteados
        private NetworkList<FixedString32Bytes> _completedTrades = new NetworkList<FixedString32Bytes>();
        private HashSet<string> _localCompletedTrades = new HashSet<string>();

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        private void Awake()
        {
            if (Instance == null) Instance = this;

            bool isPlayer = CompareTag("Player") ||
                            (transform.root != null && transform.root.CompareTag("Player")) ||
                            GetComponentInParent<InventoryController>() != null;

            if (isPlayer)
            {
                LocalInstance = this;
                _isPlayerInRange = true;
                requireProximity = false;
            }
            else if (requireProximity && GetComponent<Collider>() == null)
            {
                var col = gameObject.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(5, 5, 5);
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
            if (LocalInstance == this) LocalInstance = null;
        }

        public override void OnNetworkSpawn()
        {
            bool isPlayer = CompareTag("Player") ||
                            (transform.root != null && transform.root.CompareTag("Player")) ||
                            GetComponentInParent<InventoryController>() != null;

            if (isPlayer && IsOwner)
            {
                LocalInstance = this;
            }

            if (IsServer && _completedTrades == null)
            {
                _completedTrades = new NetworkList<FixedString32Bytes>();
            }

            if (IsClient && _completedTrades != null)
            {
                _completedTrades.OnListChanged += OnTradesListChanged;
                SyncLocalTradesWithNetwork();
            }
        }

        private void OnTradesListChanged(NetworkListEvent<FixedString32Bytes> changeEvent)
        {
            if (changeEvent.Type == NetworkListEvent<FixedString32Bytes>.EventType.Add)
            {
                if (_localCompletedTrades == null) _localCompletedTrades = new HashSet<string>();
                _localCompletedTrades.Add(changeEvent.Value.ToString());
            }
        }

        private void SyncLocalTradesWithNetwork()
        {
            if (_completedTrades == null) return;
            if (_localCompletedTrades == null) _localCompletedTrades = new HashSet<string>();

            foreach (var id in _completedTrades)
            {
                _localCompletedTrades.Add(id.ToString());
            }
        }

        public static string GetTradeIdentifier(TradeData trade)
        {
            if (trade == null) return "";
            if (!string.IsNullOrEmpty(trade.name)) return trade.name;
            if (trade.OutputItem != null && !string.IsNullOrEmpty(trade.OutputItem.itemName))
                return trade.OutputItem.itemName;
            return trade.ToString();
        }

        public bool IsTradeCompleted(TradeData trade)
        {
            if (trade == null) return false;
            return IsTradeCompleted(GetTradeIdentifier(trade));
        }

        public bool IsTradeCompleted(string id)
        {
            if (string.IsNullOrEmpty(id) || _localCompletedTrades == null) return false;
            string cleanId = id.Trim().ToLowerInvariant();
            foreach (var completed in _localCompletedTrades)
            {
                if (completed.Trim().ToLowerInvariant() == cleanId)
                    return true;
            }
            return false;
        }

        public static bool IsTradeCompletedAnywhere(TradeData trade)
        {
            if (trade == null) return false;
            if (LocalInstance != null && LocalInstance.IsTradeCompleted(trade)) return true;
            if (Instance != null && Instance.IsTradeCompleted(trade)) return true;

            var allCraftingControllers = Object.FindObjectsByType<CraftingController>(FindObjectsSortMode.None);
            foreach (var cc in allCraftingControllers)
            {
                if (cc != null && cc.IsTradeCompleted(trade)) return true;
            }
            return false;
        }

        public void RecordCompletedTrade(TradeData trade)
        {
            if (trade == null) return;
            string id = GetTradeIdentifier(trade);
            if (string.IsNullOrEmpty(id)) return;

            if (_localCompletedTrades == null) _localCompletedTrades = new HashSet<string>();
            _localCompletedTrades.Add(id);

            if (IsSpawned && _completedTrades != null)
            {
                RecordTradeServerRpc(id);
            }

            if (LocalInstance != null && LocalInstance != this)
            {
                LocalInstance.RecordCompletedTrade(trade);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RecordTradeServerRpc(string tradeId)
        {
            if (!IsTradeCompleted(tradeId) && _completedTrades != null)
            {
                _completedTrades.Add(tradeId);
            }
        }

        private void Update()
        {
            // Solo el jugador local (propietario) debe procesar la entrada del teclado
            bool isPlayer = CompareTag("Player") ||
                            (transform.root != null && transform.root.CompareTag("Player")) ||
                            GetComponentInParent<InventoryController>() != null;

            if (!isPlayer) return;
            if (IsNetworkActive && !IsOwner) return;

            if (Keyboard.current == null) return;

            // ACTIVACIÓN (Tecla T o C)
            if (Keyboard.current.tKey.wasPressedThisFrame || Keyboard.current.cKey.wasPressedThisFrame)
            {
                ToggleCraftingUI();
            }
        }

        private void ToggleCraftingUI()
        {
            var allControllers = Object.FindObjectsByType<CraftingController>(FindObjectsSortMode.None);
            var nearbyZone = allControllers.FirstOrDefault(x => x != null && x.requireProximity && x._isPlayerInRange);

            if (nearbyZone != null)
            {
                nearbyZone.SetOpen(!nearbyZone._open);
            }
            else if (LocalInstance != null)
            {
                LocalInstance.SetOpen(!LocalInstance._open);
            }
            else
            {
                SetOpen(!_open);
            }
        }

        public void SetOpen(bool open)
        {
            if (open)
            {
                var allControllers = Object.FindObjectsByType<CraftingController>(FindObjectsSortMode.None);
                foreach (var cc in allControllers)
                {
                    if (cc != null && cc != this)
                    {
                        cc._open = false;
                    }
                }
            }

            _open = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
            if (!open) _selectedRecipeIndex = -1;
        }

        #region Trigger Proximity
        private bool IsLocalPlayerCollider(Collider other)
        {
            var inv = other.GetComponentInParent<InventoryController>() ?? other.GetComponent<InventoryController>();
            if (inv != null)
            {
                return InventoryController.LocalInstance == inv || (inv.IsSpawned && inv.IsOwner);
            }
            return other.CompareTag("Player");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsLocalPlayerCollider(other))
            {
                _isPlayerInRange = true;
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (IsLocalPlayerCollider(other))
            {
                _isPlayerInRange = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsLocalPlayerCollider(other))
            {
                if (requireProximity)
                {
                    _isPlayerInRange = false;
                    if (_open) SetOpen(false);
                }
            }
        }
        #endregion

        #region UI Rendering
        private void OnGUI()
        {
            if (_open)
            {
                EnsureStyles();
                DrawExpandedUI();
                return;
            }

            if (requireProximity && _isPlayerInRange)
            {
                EnsureStyles();
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

            if (availableTrades == null) availableTrades = new List<TradeData>();

            GUI.BeginGroup(listRect);
            _scrollPos = GUI.BeginScrollView(new Rect(0, 0, listRect.width, listRect.height), _scrollPos, new Rect(0, 0, listRect.width - 20, availableTrades.Count * 55));
            for (int i = 0; i < availableTrades.Count; i++)
            {
                if (availableTrades[i] == null) continue;
                Rect r = new Rect(0, i * 55, listRect.width - 20, 50);
                bool isSelected = (_selectedRecipeIndex == i);
                GUI.DrawTexture(r, isSelected ? _texSelected : _texSlot);

                ItemData displayItem = availableTrades[i].OutputItem ?? availableTrades[i].InputItem;
                if (displayItem != null)
                {
                    if (displayItem.itemSprite != null)
                        GUI.DrawTexture(new Rect(5, i * 55 + 5, 40, 40), displayItem.itemSprite.texture);
                    GUI.Label(new Rect(50, i * 55, listRect.width - 60, 50), displayItem.itemName, _recipeSty);
                }
                else
                {
                    GUI.Label(new Rect(10, i * 55, listRect.width - 20, 50), availableTrades[i].name, _recipeSty);
                }

                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    _selectedRecipeIndex = i;
                    Event.current.Use();
                }
            }
            GUI.EndScrollView();
            GUI.EndGroup();

            if (_selectedRecipeIndex >= 0 && _selectedRecipeIndex < availableTrades.Count && availableTrades[_selectedRecipeIndex] != null)
            {
                TradeData recipe = availableTrades[_selectedRecipeIndex];
                GUI.BeginGroup(detailRect);
                float y = 0;
                GUI.Label(new Rect(0, y, detailRect.width, 22), "REQUIERE:", _infoSty); y += 25;

                if (recipe.inputs != null && recipe.inputs.Count > 0)
                {
                    foreach (var inReq in recipe.inputs)
                    {
                        if (inReq == null || inReq.item == null) continue;
                        GUI.DrawTexture(new Rect(0, y, 40, 40), _texSlot);
                        if (inReq.item.itemSprite != null) GUI.DrawTexture(new Rect(2, y + 2, 36, 36), inReq.item.itemSprite.texture);
                        string qtyText = inReq.useRange ? $"x{inReq.minAmount}-{inReq.maxAmount}" : $"x{inReq.amount}";
                        GUI.Label(new Rect(0, y, 40, 40), qtyText, _qtySty);
                        GUI.Label(new Rect(50, y + 8, detailRect.width - 50, 25), inReq.item.itemName, _recipeSty);
                        y += 45;
                    }
                }

                GUI.Label(new Rect(detailRect.width / 2 - 15, y - 5, 30, 25), "↓", _titleSty); y += 25;
                GUI.Label(new Rect(0, y, detailRect.width, 22), "OBTIENES:", _infoSty); y += 25;

                if (recipe.outputs != null && recipe.outputs.Count > 0)
                {
                    foreach (var outReq in recipe.outputs)
                    {
                        if (outReq == null || outReq.item == null) continue;
                        GUI.DrawTexture(new Rect(0, y, 40, 40), _texSlot);
                        if (outReq.item.itemSprite != null) GUI.DrawTexture(new Rect(2, y + 2, 36, 36), outReq.item.itemSprite.texture);
                        string qtyText = outReq.useRange ? $"x{outReq.minAmount}-{outReq.maxAmount}" : $"x{outReq.amount}";
                        GUI.Label(new Rect(0, y, 40, 40), qtyText, _qtySty);
                        GUI.Label(new Rect(50, y + 8, detailRect.width - 50, 25), outReq.item.itemName, _recipeSty);
                        y += 45;
                    }
                }

                y += 10;
                Rect btnR = new Rect(0, y, detailRect.width, 45);
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
                Debug.LogWarning("[Crafting] Materiales insuficientes para " + (recipe.OutputItem != null ? recipe.OutputItem.itemName : recipe.name));
            }
        }

        private bool CanCraft(TradeData recipe)
        {
            if (recipe == null) return false;
            var bag = InventoryController.GetBag();

            if (recipe.inputs != null && recipe.inputs.Count > 0)
            {
                foreach (var inReq in recipe.inputs)
                {
                    if (inReq == null || inReq.item == null) continue;
                    int requiredAmount = inReq.GetMinRequiredAmount();
                    if (requiredAmount <= 0) continue;

                    int qtyInBag = MissionController.GetItemQuantityInBag(bag, inReq.item);
                    if (qtyInBag < requiredAmount) return false;
                }
                return true;
            }

            return false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestTradeServerRpc(int recipeId, ulong clientId, ulong targetId) => ExecuteTradeLocal(recipeId, clientId, targetId);

        public void ExecuteTradeLocal(int recipeId, ulong clientId, ulong targetId)
        {
            if (recipeId < 0 || recipeId >= availableTrades.Count) return;
            TradeData recipe = availableTrades[recipeId];
            if (recipe == null) return;

            var allInvs = Object.FindObjectsByType<InventoryController>(FindObjectsSortMode.None);
            var crafterInv = allInvs.FirstOrDefault(x => x.OwnerClientId == clientId);
            var receiverInv = allInvs.FirstOrDefault(x => x.OwnerClientId == targetId);

            if (crafterInv == null) crafterInv = InventoryController.LocalInstance;
            if (receiverInv == null) receiverInv = crafterInv;

            // 1. Remover insumos
            if (crafterInv != null && recipe.inputs != null)
            {
                foreach (var inReq in recipe.inputs)
                {
                    if (inReq == null || inReq.item == null) continue;
                    int amountToRemove = inReq.GetAmount();
                    if (amountToRemove <= 0) continue;

                    if (IsNetworkActive)
                        crafterInv.RemoveItemServerRpc(inReq.item.GetHashCode(), amountToRemove);
                    else
                        crafterInv.InternalAddItem(inReq.item.GetHashCode(), -amountToRemove);
                }
            }

            // 2. Otorgar productos
            if (receiverInv != null && recipe.outputs != null)
            {
                foreach (var outReq in recipe.outputs)
                {
                    if (outReq == null || outReq.item == null) continue;
                    int amountToAdd = outReq.GetAmount();
                    if (amountToAdd <= 0) continue;

                    if (IsNetworkActive)
                        receiverInv.AddItemServerRpc(outReq.item.GetHashCode(), amountToAdd);
                    else
                        receiverInv.InternalAddItem(outReq.item.GetHashCode(), amountToAdd);
                }
            }

            // 3. Registrar el crafteo/trade como realizado
            RecordCompletedTrade(recipe);

            InventoryController.MarkCountDirty();

            // Actualizar misiones para verificar si este crafteo completa una misión activa
            if (MissionController.Instance != null)
            {
                MissionController.Instance.UpdateMissionFlow();
            }
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
