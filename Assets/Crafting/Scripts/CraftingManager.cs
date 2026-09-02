using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using System.Collections.Generic;
using NGO.Data;
using System.Linq;

namespace Crafting.Scripts
{
    public class CraftingManager : NetworkBehaviour
    {
        public static CraftingManager Instance { get; private set; }

        public bool IsUIOpen => _open;

        [Header("Settings")]
        public List<TradeData> availableTrades;

        [Header("UI Aesthetics")]
        public int panelWidth = 500;
        public int panelHeight = 550;
        public int titleH = 65;
        public int padding = 20;
        public int cornerRadius = 15;
        public Color panelColor = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        public Color accentColor = new Color(1f, 0.85f, 0f, 1f);

        private bool _open;
        private Vector2 _scrollPos;
        private int _selectedRecipeIndex = -1;

        // Styles & Textures
        private Texture2D _texPanel, _texSlot, _texSelected, _texBtnNormal, _texBtnHover;
        private GUIStyle _titleSty, _recipeSty, _btnSty, _infoSty, _qtySty;
        private bool _stylesReady;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
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
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
            if (!open) _selectedRecipeIndex = -1;
        }

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

            float screenW = Screen.width;
            float screenH = Screen.height;

            // Encontrar todos los inventories en la escena
            var allInventories = Object.FindObjectsByType<InventoryController>(FindObjectsSortMode.None);
            var myInv = InventoryController.LocalInstance;
            var otherInvs = allInventories.Where(x => x != myInv).ToList();

            // Layout de 3 columnas
            float panelW = 450; // Ancho por panel
            float totalW = panelW * 3 + 40; // 3 paneles + gaps
            float xStart = (screenW - totalW) / 2f;
            float y0 = (screenH - panelHeight) / 2f;

            // 1. Panel Izquierda: Mi Inventario
            if (myInv != null)
            {
                myInv.DrawInventoryUI(new Rect(xStart, y0, panelW, panelHeight), "MI INVENTARIO");
            }

            // 2. Panel Centro: Crafting
            Rect centerRect = new Rect(xStart + panelW + 20, y0, panelW, panelHeight);
            DrawCraftingPanel(centerRect);

            // 3. Panel Derecha: Inventario de otro player (si hay)
            if (otherInvs.Count > 0)
            {
                otherInvs[0].DrawInventoryUI(new Rect(xStart + (panelW + 20) * 2, y0, panelW, panelHeight), "INVENTARIO REMOTO");
            }
            else
            {
                // Placeholder si no hay nadie más crafteando
                GUI.DrawTexture(new Rect(xStart + (panelW + 20) * 2, y0, panelW, panelHeight), _texPanel);
                GUI.Label(new Rect(xStart + (panelW + 20) * 2, y0, panelW, panelHeight), "ESPERANDO A OTRO JUGADOR PARA INTERCAMBIAR...", _infoSty);
            }

            // Botón cerrar global
            if (GUI.Button(new Rect(screenW / 2 + totalW / 2 - 50, y0 + 15, 35, 35), "X", _btnSty)) SetOpen(false);
        }

        private void DrawCraftingPanel(Rect rect)
        {
            GUI.DrawTexture(rect, _texPanel);
            GUI.Label(new Rect(rect.x, rect.y + 10, rect.width, titleH), "ESTACIÓN DE TRABAJO", _titleSty);

            float paddingInner = 20;
            Rect listRect = new Rect(rect.x + paddingInner, rect.y + titleH + 10, rect.width * 0.45f, rect.height - titleH - 30);
            Rect detailRect = new Rect(rect.x + rect.width * 0.5f, rect.y + titleH + 10, rect.width * 0.45f, rect.height - titleH - 30);

            // Lista de recetas (scrollable)
            GUI.BeginGroup(listRect);
            _scrollPos = GUI.BeginScrollView(new Rect(0, 0, listRect.width, listRect.height), _scrollPos, new Rect(0, 0, listRect.width - 20, availableTrades.Count * 55));
            for (int i = 0; i < availableTrades.Count; i++)
            {
                Rect r = new Rect(0, i * 55, listRect.width - 20, 50);
                bool isSelected = (_selectedRecipeIndex == i);
                GUI.DrawTexture(r, isSelected ? _texSelected : _texSlot);
                if (availableTrades[i].OutputItem != null)
                {
                    if (availableTrades[i].OutputItem.icon != null)
                        GUI.DrawTexture(new Rect(5, i * 55 + 5, 40, 40), availableTrades[i].OutputItem.icon.texture);
                    GUI.Label(new Rect(50, i * 55, listRect.width - 60, 50), availableTrades[i].OutputItem.displayName, _recipeSty);
                }
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    _selectedRecipeIndex = i;
                    Event.current.Use();
                }
            }
            GUI.EndScrollView();
            GUI.EndGroup();

            // Detalle de receta
            if (_selectedRecipeIndex >= 0)
            {
                TradeData recipe = availableTrades[_selectedRecipeIndex];
                GUI.BeginGroup(detailRect);
                float y = 0;
                GUI.Label(new Rect(0, y, detailRect.width, 25), "REQUIERE:", _infoSty); y += 30;
                GUI.DrawTexture(new Rect(0, y, 60, 60), _texSlot);
                if (recipe.InputItem.icon != null) GUI.DrawTexture(new Rect(5, y + 5, 50, 50), recipe.InputItem.icon.texture);
                GUI.Label(new Rect(0, y, 60, 60), "x" + recipe.InputAmount, _qtySty);
                GUI.Label(new Rect(70, y + 15, detailRect.width - 70, 30), recipe.InputItem.displayName, _recipeSty);
                y += 75;
                GUI.Label(new Rect(detailRect.width / 2 - 15, y - 5, 30, 30), "↓", _titleSty); y += 30;
                GUI.Label(new Rect(0, y, detailRect.width, 25), "OBTIENES:", _infoSty); y += 30;
                GUI.DrawTexture(new Rect(0, y, 60, 60), _texSlot);
                if (recipe.OutputItem.icon != null) GUI.DrawTexture(new Rect(5, y + 5, 50, 50), recipe.OutputItem.icon.texture);
                GUI.Label(new Rect(0, y, 60, 60), "x" + recipe.OutputAmount, _qtySty);
                GUI.Label(new Rect(70, y + 15, detailRect.width - 70, 30), recipe.OutputItem.displayName, _recipeSty);
                y += 85;
                Rect btnR = new Rect(0, y, detailRect.width, 50);
                GUI.DrawTexture(btnR, btnR.Contains(Event.current.mousePosition) ? _texBtnHover : _texBtnNormal);
                if (GUI.Button(btnR, "CRAFTEAR", _btnSty)) TryExecuteTrade(_selectedRecipeIndex);
                GUI.EndGroup();
            }
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
