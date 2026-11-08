using UnityEngine;
using Runtime.Scripts.Gate;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Collections;
using System.Collections.Generic;
using System.Linq;
using Runtime.Scripts.Trades;
using Runtime.Scripts.Inventory;
using Runtime.Scripts.Narrative;
using Runtime.Scripts.Items;
using Runtime.Scripts.Singleton;

namespace Runtime.Scripts.Crafting
{
    /// <summary>
    /// Unified controller for the Crafting System.
    /// Handles UI, trigger proximity, player-attached crafting, and tracking completed trades.
    /// </summary>
    public class CraftingController : CSingletonNB<CraftingController>
    {
        public static CraftingController LocalInstance => Instance;

        public bool IsUIOpen => _open;

        [Header("System Settings")]
        public List<TradesData> availableTrades;
        public bool requireProximity = true;

        [Header("UI Aesthetics")]
        public int panelWidth = 500;
        public int panelHeight = 550;
        public int titleH = 65;
        public int padding = 20;
        [Tooltip("Escala de todo el crafting (se limita para que quepan los 3 paneles).")] public float uiScale = 2f;
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

        protected override void Awake()
        {
            base.Awake();

            bool isPlayer = CompareTag("Player") ||
                            (transform.root != null && transform.root.CompareTag("Player")) ||
                            GetComponentInParent<InventoryController>() != null;

            if (isPlayer)
            {
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

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

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

        public static string GetTradeIdentifier(TradesData trade)
        {
            if (trade == null) return "";
            if (!string.IsNullOrEmpty(trade.name)) return trade.name;
            if (trade.OutputItem != null && !string.IsNullOrEmpty(trade.OutputItem.itemName))
                return trade.OutputItem.itemName;
            return trade.ToString();
        }

        public bool IsTradeCompleted(TradesData trade)
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

        public static bool IsTradeCompletedAnywhere(TradesData trade)
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

        public void RecordCompletedTrade(TradesData trade)
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

        private void OnGate(string k) { if (k == "crafting" && _open) SetOpen(false); }

        public void SetOpen(bool open)
        {
            if (open && !MenuGate.TryOpen("crafting")) return; // otro menú abierto
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

            if (!open && _open) MenuGate.Close("crafting");
            MenuGate.CloseRequested -= OnGate;
            if (open) MenuGate.CloseRequested += OnGate;
            _open = open;
            bool free = open || MenuGate.Busy;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
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

        const float SideW = 400f;
        float Kc => Mathf.Min(uiScale, (Screen.width - 40f) / (SideW * 2f + panelWidth + 40f), (Screen.height - 40f) / panelHeight);

        // Marco metalico redondeado + pantalla negra.
        private void Frame(Rect r, float m)
        {
            GUI.DrawTexture(r, MenuGate.Metal(Mathf.Max(8, (int)(r.width / 2f)), Mathf.Max(8, (int)(r.height / 2f))));
            GUI.DrawTexture(new Rect(r.x + m, r.y + m, r.width - 2f * m, r.height - 2f * m), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(0.01f, 0.03f, 0.02f), 0f, Mathf.Min(10f * Kc, (r.height - 2f * m) / 2f));
        }

        private void DrawMinimizedUI()
        {
            float k = Kc, w = minWidth * k, h = minHeight * k;
            Rect rect = new Rect((Screen.width - w) / 2f, Screen.height - h - 20f, w, h);
            Frame(rect, 6f * k);
            GUI.Label(rect, "PRESIONA [C] PARA CRAFTEAR", _minSty);
        }

        private void DrawExpandedUI()
        {
            float k = Kc, sw = SideW * k, cw = panelWidth * k, ph = panelHeight * k, gap = 20f * k;
            float totalW = sw * 2f + cw + gap * 2f;
            float xStart = (Screen.width - totalW) / 2f, y0 = (Screen.height - ph) / 2f;

            var allInvs = Object.FindObjectsByType<InventoryController>(FindObjectsSortMode.None);
            var myInv = InventoryController.LocalInstance;
            var otherInvs = allInvs.Where(x => x != myInv).ToList();

            if (myInv != null) myInv.DrawInventoryUI(new Rect(xStart, y0, sw, ph), "MI INVENTARIO", k);

            DrawCraftingPanel(new Rect(xStart + sw + gap, y0, cw, ph), k);

            Rect right = new Rect(xStart + sw + cw + 2f * gap, y0, sw, ph);
            if (otherInvs.Count > 0) otherInvs[0].DrawInventoryUI(right, "INVENTARIO COMPAÑERO", k);
            else
            {
                Frame(right, 16f * k);
                GUI.Label(right, "ESPERANDO A OTRO JUGADOR...", _minSty);
            }

            if (GUI.Button(new Rect(xStart + totalW - 56f * k, y0 + 22f * k, 35f * k, 35f * k), "X", _btnSty)) SetOpen(false);
        }

        private void DrawCraftingPanel(Rect rect, float k)
        {
            Frame(rect, 16f * k);
            GUI.Label(new Rect(rect.x, rect.y + 10f * k, rect.width, titleH * k), "ESTACIÓN DE TRABAJO", _titleSty);

            float top = rect.y + titleH * k + 10f * k, hh = rect.height - titleH * k - 50f * k;
            Rect listRect = new Rect(rect.x + 28f * k, top, rect.width * 0.45f, hh);
            Rect detailRect = new Rect(rect.x + rect.width * 0.5f, top, rect.width * 0.45f, hh);
            float row = 70f * k;

            if (availableTrades == null) availableTrades = new List<TradesData>();

            GUI.BeginGroup(listRect);
            _scrollPos = GUI.BeginScrollView(new Rect(0, 0, listRect.width, listRect.height), _scrollPos, new Rect(0, 0, listRect.width - 20f * k, availableTrades.Count * row));
            for (int i = 0; i < availableTrades.Count; i++)
            {
                if (availableTrades[i] == null) continue;
                Rect r = new Rect(0, i * row, listRect.width - 20f * k, row - 6f * k);
                GUI.DrawTexture(r, _selectedRecipeIndex == i ? _texSelected : _texSlot);

                ItemsData displayItem = availableTrades[i].OutputItem ?? availableTrades[i].InputItem;
                if (displayItem != null)
                {
                    float ic = r.height - 12f * k;
                    if (displayItem.itemSprite != null) GUI.DrawTexture(new Rect(6f * k, r.y + 6f * k, ic, ic), displayItem.itemSprite.texture);
                    GUI.Label(new Rect(ic + 14f * k, r.y, r.width - ic - 20f * k, r.height), displayItem.itemName, _recipeSty);
                }
                else GUI.Label(new Rect(10f * k, r.y, r.width - 20f * k, r.height), availableTrades[i].name, _recipeSty);

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
                TradesData recipe = availableTrades[_selectedRecipeIndex];
                GUI.BeginGroup(detailRect);
                float y = 0f, sl = 56f * k, dw = detailRect.width;
                GUI.Label(new Rect(0, y, dw, 26f * k), "REQUIERE:", _infoSty); y += 30f * k;
                if (recipe.inputs != null)
                    foreach (var q in recipe.inputs)
                        if (q != null && q.item != null) y = Line(q.item, q.useRange ? $"x{q.minAmount}-{q.maxAmount}" : $"x{q.amount}", y, sl, dw, k);

                GUI.Label(new Rect(dw / 2f - 15f * k, y - 5f * k, 30f * k, 28f * k), "↓", _titleSty); y += 28f * k;
                GUI.Label(new Rect(0, y, dw, 26f * k), "OBTIENES:", _infoSty); y += 30f * k;
                if (recipe.outputs != null)
                    foreach (var q in recipe.outputs)
                        if (q != null && q.item != null) y = Line(q.item, q.useRange ? $"x{q.minAmount}-{q.maxAmount}" : $"x{q.amount}", y, sl, dw, k);

                y += 10f * k;
                Rect btnR = new Rect(0, y, dw, 50f * k);
                GUI.DrawTexture(btnR, btnR.Contains(Event.current.mousePosition) ? _texBtnHover : _texBtnNormal);
                if (GUI.Button(btnR, "CRAFTEAR", _btnSty)) TryExecuteTrade(_selectedRecipeIndex);
                GUI.EndGroup();
            }
        }

        // Una linea de receta: icono grande + cantidad + nombre. Devuelve la nueva y.
        private float Line(ItemsData item, string qty, float y, float sl, float dw, float k)
        {
            GUI.DrawTexture(new Rect(0, y, sl, sl), _texSlot);
            if (item.itemSprite != null) GUI.DrawTexture(new Rect(4f * k, y + 4f * k, sl - 8f * k, sl - 8f * k), item.itemSprite.texture);
            GUI.Label(new Rect(0, y, sl, sl), qty, _qtySty);
            GUI.Label(new Rect(sl + 10f * k, y + (sl - 28f * k) / 2f, dw - sl - 10f * k, 28f * k), item.itemName, _recipeSty);
            return y + sl + 8f * k;
        }

        #endregion

        #region Trade Logic
        private void TryExecuteTrade(int index)
        {
            if (index < 0 || index >= availableTrades.Count) return;

            TradesData recipe = availableTrades[index];
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

        private bool CanCraft(TradesData recipe)
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

                    int qtyInBag = StoryManager.Qty(bag, inReq.item);
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
            TradesData recipe = availableTrades[recipeId];
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
        }
        #endregion

        #region Helpers
        private void EnsureStyles()
        {
            if (_stylesReady) return;
            float k = Kc;
            _texSlot = MakeRoundedTex(64, 8, new Color(0.03f, 0.12f, 0.05f), Color.clear, 0);
            _texSelected = MakeRoundedTex(64, 8, new Color(0.05f, 0.2f, 0.08f), MenuGate.Green, 2);
            _texBtnNormal = MakeRoundedTex(64, 10, new Color(0.05f, 0.2f, 0.08f), MenuGate.Green, 1);
            _texBtnHover = MakeRoundedTex(64, 10, new Color(0.08f, 0.3f, 0.12f), MenuGate.Green, 2);
            _titleSty = Sty((int)(32 * k), FontStyle.Bold, TextAnchor.MiddleCenter, MenuGate.Green);
            _recipeSty = Sty((int)(18 * k), FontStyle.Normal, TextAnchor.MiddleLeft, MenuGate.Green);
            _btnSty = Sty((int)(20 * k), FontStyle.Bold, TextAnchor.MiddleCenter, MenuGate.Green);
            _infoSty = Sty((int)(16 * k), FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.2f, 1f, 0.35f, 0.6f));
            _qtySty = Sty((int)(14 * k), FontStyle.Bold, TextAnchor.LowerRight, MenuGate.Green);

            _minSty = new GUIStyle(_infoSty)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = (int)(18 * k)
            };
            _minSty.normal.textColor = MenuGate.Green;

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