using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using System.Collections.Generic;
using NGO.Data;

namespace Crafting.Scripts
{
    public class CraftingManager : NetworkBehaviour
    {
        public static CraftingManager Instance { get; private set; }

        [Header("Settings")]
        public List<TradeData> availableTrades;

        [Header("UI Aesthetics")]
        public int panelWidth = 700;
        public int panelHeight = 550;
        public int titleH = 65;
        public int padding = 20;
        public int cornerRadius = 15;
        public Color panelColor = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        public Color accentColor = new Color(1f, 0.85f, 0f, 1f);

        private bool _open;
        private Vector2 _scrollPos;
        private int _selectedRecipeIndex = -1;

        // Styles & Textures (matching InventoryController style)
        private Texture2D _texPanel, _texSlot, _texSelected, _texBtnNormal, _texBtnHover;
        private GUIStyle _titleSty, _recipeSty, _btnSty, _infoSty, _qtySty;
        private bool _stylesReady;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public override void OnNetworkSpawn()
        {
            // Opcional: sincronizar estado global del mercado si fuera necesario
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            {
                SetOpen(!_open);
            }
        }

        private void SetOpen(bool open)
        {
            _open = open;
            // Bloquear/Desbloquear cursor similar al inventario
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;

            if (!open) _selectedRecipeIndex = -1;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            // Textures
            _texPanel = MakeRoundedTex(64, cornerRadius, panelColor, Color.clear, 0);
            _texSlot = MakeRoundedTex(64, 8, new Color(1f, 1f, 1f, 0.08f), Color.clear, 0);
            _texSelected = MakeRoundedTex(64, 8, new Color(1f, 1f, 1f, 0.15f), accentColor, 2);
            _texBtnNormal = MakeRoundedTex(64, 10, new Color(0.2f, 0.2f, 0.25f, 1f), Color.white, 1);
            _texBtnHover = MakeRoundedTex(64, 10, new Color(0.3f, 0.3f, 0.4f, 1f), accentColor, 2);

            // Styles
            _titleSty = Sty(32, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            _recipeSty = Sty(18, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
            _btnSty = Sty(20, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            _infoSty = Sty(16, FontStyle.Italic, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.8f));
            _qtySty = Sty(14, FontStyle.Bold, TextAnchor.LowerRight, accentColor);

            _stylesReady = true;
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

        private void OnGUI()
        {
            if (!_open) return;

            EnsureStyles();

            float x0 = (Screen.width - panelWidth) / 2f;
            float y0 = (Screen.height - panelHeight) / 2f;
            Rect panelRect = new Rect(x0, y0, panelWidth, panelHeight);

            GUI.DrawTexture(panelRect, _texPanel);
            GUI.Label(new Rect(x0, y0 + 10, panelWidth, titleH), "CENTRO DE CRAFTEO & TRADE", _titleSty);

            // Close button
            if (GUI.Button(new Rect(x0 + panelWidth - 50, y0 + 15, 35, 35), "X", _btnSty)) SetOpen(false);

            // Recipe List Area
            Rect listRect = new Rect(x0 + padding, y0 + titleH + padding, panelWidth * 0.4f, panelHeight - titleH - padding * 2);
            DrawRecipeList(listRect);

            // Selection Detail Area
            Rect detailRect = new Rect(x0 + panelWidth * 0.45f + padding, y0 + titleH + padding, panelWidth * 0.5f - padding * 2, panelHeight - titleH - padding * 2);
            DrawRecipeDetail(detailRect);
        }

        private void DrawRecipeList(Rect rect)
        {
            GUI.BeginGroup(rect);
            _scrollPos = GUI.BeginScrollView(new Rect(0, 0, rect.width, rect.height), _scrollPos, new Rect(0, 0, rect.width - 20, availableTrades.Count * 60));

            for (int i = 0; i < availableTrades.Count; i++)
            {
                Rect r = new Rect(0, i * 60, rect.width - 20, 55);
                bool isSelected = (_selectedRecipeIndex == i);

                GUI.DrawTexture(r, isSelected ? _texSelected : _texSlot);

                if (availableTrades[i].OutputItem != null)
                {
                    if (availableTrades[i].OutputItem.icon != null)
                        GUI.DrawTexture(new Rect(5, i * 60 + 7, 40, 40), availableTrades[i].OutputItem.icon.texture);

                    GUI.Label(new Rect(55, i * 60, rect.width - 70, 55), availableTrades[i].OutputItem.displayName, _recipeSty);
                }

                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    _selectedRecipeIndex = i;
                    Event.current.Use();
                }
            }

            GUI.EndScrollView();
            GUI.EndGroup();
        }

        private void DrawRecipeDetail(Rect rect)
        {
            if (_selectedRecipeIndex < 0 || _selectedRecipeIndex >= availableTrades.Count)
            {
                GUI.Label(rect, "Selecciona una receta para comenzar", _infoSty);
                return;
            }

            TradeData recipe = availableTrades[_selectedRecipeIndex];
            GUI.BeginGroup(rect);

            float y = 0;
            GUI.Label(new Rect(0, y, rect.width, 30), "REQUERIMIENTO:", _infoSty);
            y += 35;

            // Input Item Box
            Rect inputRect = new Rect(0, y, 80, 80);
            GUI.DrawTexture(inputRect, _texSlot);
            if (recipe.InputItem != null && recipe.InputItem.icon != null)
            {
                GUI.DrawTexture(new Rect(10, y + 10, 60, 60), recipe.InputItem.icon.texture);
                GUI.Label(inputRect, "x" + recipe.InputAmount, _qtySty);
                GUI.Label(new Rect(90, y + 25, rect.width - 90, 30), recipe.InputItem.displayName, _recipeSty);
            }

            y += 100;
            GUI.Label(new Rect(rect.width / 2 - 20, y - 10, 40, 40), "↓", _titleSty);
            y += 40;

            GUI.Label(new Rect(0, y, rect.width, 30), "RESULTADO:", _infoSty);
            y += 35;

            // Output Item Box
            Rect outputRect = new Rect(0, y, 80, 80);
            GUI.DrawTexture(outputRect, _texSlot);
            if (recipe.OutputItem != null && recipe.OutputItem.icon != null)
            {
                GUI.DrawTexture(new Rect(10, y + 10, 60, 60), recipe.OutputItem.icon.texture);
                GUI.Label(outputRect, "x" + recipe.OutputAmount, _qtySty);
                GUI.Label(new Rect(90, y + 25, rect.width - 90, 30), recipe.OutputItem.displayName, _recipeSty);
            }

            y += 110;

            // Craft Button
            Rect btnRect = new Rect(rect.width * 0.1f, y, rect.width * 0.8f, 60);
            bool hover = btnRect.Contains(Event.current.mousePosition);
            GUI.DrawTexture(btnRect, hover ? _texBtnHover : _texBtnNormal);
            if (GUI.Button(btnRect, "CRAFTEAR AHORA", _btnSty))
            {
                TryExecuteTrade(_selectedRecipeIndex);
            }

            GUI.EndGroup();
        }

        private void TryExecuteTrade(int index)
        {
            if (index < 0 || index >= availableTrades.Count) return;

            TradeData recipe = availableTrades[index];
            ulong myId = (NetworkManager.Singleton != null) ? NetworkManager.Singleton.LocalClientId : 0;

            // Validación rápida antes de enviar al servidor (opcional, pero buena práctica)
            if (CanCraft(recipe))
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
                {
                    RequestTradeServerRpc(index, myId);
                }
                else
                {
                    // Fallback Offline
                    ExecuteTradeLocal(index, myId);
                }
            }
            else
            {
                Debug.LogWarning("[Crafting] Materiales insuficientes para " + recipe.OutputItem.displayName);
            }
        }

        private bool CanCraft(TradeData recipe)
        {
            var bag = InventoryController.GetBag();
            string key = recipe.InputItem.itemCode.ToLowerInvariant();

            // Buscar en el inventario persistente
            if (bag.TryGetValue(key, out var slot))
            {
                return slot.qty >= recipe.InputAmount;
            }
            return false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestTradeServerRpc(int recipeId, ulong clientId)
        {
            // El servidor procesa la lógica
            ExecuteTradeLocal(recipeId, clientId);
        }

        public void ExecuteTradeLocal(int recipeId, ulong clientId)
        {
            if (recipeId < 0 || recipeId >= availableTrades.Count) return;

            TradeData recipe = availableTrades[recipeId];
            Debug.Log($"[Server/Local] Procesando tradeo {recipe.name} para cliente {clientId}");

            // 1. Quitar ingredientes
            string inputKey = recipe.InputItem.itemCode.ToLowerInvariant();
            for (int i = 0; i < recipe.InputAmount; i++)
            {
                InventoryController.RemoveItem(inputKey);
            }

            // 2. Añadir resultado
            for (int i = 0; i < recipe.OutputAmount; i++)
            {
                InventoryController.Add(recipe.OutputItem);
            }

            // Si estamos en el cliente, forzar refresco de UI si fuera necesario
            InventoryController.MarkCountDirty();
        }

        // --- Market Logic (Placeholder centralizado) ---
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void OfferItemMarketServerRpc(string itemCode, int quantity, ulong senderId)
        {
            Debug.Log($"[Market] Jugador {senderId} ofrece {quantity}x {itemCode}");
        }
    }
}
