using UnityEngine;
using UnityEngine.Serialization;
using Unity.Netcode;
using Unity.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using Missions.Data;
using Crafting.Scripts;
using Trades.Data;
using Menus.Scripts;
using Narrative.Scripts;

namespace Missions.Scripts
{
    /// <summary>
    /// Controlador y Manager de Misiones.
    /// Mantiene firme la misión activa hasta que se complete, antes de evaluar la siguiente.
    /// </summary>
    [AddComponentMenu("Missions/Mission Controller")]
    public class MissionController : NetworkBehaviour
    {
        public static MissionController Instance { get; private set; }

        [Header("Misión Particular de este Trigger")]
        public MissionData missionData;

        [Header("Lista de Misiones / Pila de Misiones")]
        [FormerlySerializedAs("allMissions")]
        public List<MissionData> missions = new List<MissionData>();

        // Propiedad alias para compatibilidad
        public List<MissionData> allMissions { get => missions; set => missions = value; }

        [Header("Mensaje Personalizado al Activar Trigger")]
        public string customTitle;
        [TextArea] public string customDescription;

        // UI Generada dinámicamente
        private static GameObject s_SharedHudPanel;
        private static TextMeshProUGUI s_SharedTitleTMP;
        private static TextMeshProUGUI s_SharedDescTMP;

        private GameObject _hudPanel;
        private TextMeshProUGUI _titleTMP;
        private TextMeshProUGUI _descTMP;

        // Misión activa actualmente
        private MissionData _currentActiveMission;

        // Registro local y multiplayer de misiones completadas
        private NetworkList<FixedString32Bytes> _completedMissions = new NetworkList<FixedString32Bytes>();
        private HashSet<string> _localCompletedMissions = new HashSet<string>();
        private string _currentVisibleMissionId = "";
        private float _lastTriggerTime = -1f;

        private static bool s_IsQuitting;
        private bool _isDestroyed;

        private void OnApplicationQuit()
        {
            s_IsQuitting = true;
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            if (_localCompletedMissions == null)
            {
                _localCompletedMissions = new HashSet<string>();
            }
            if (_completedMissions == null)
            {
                _completedMissions = new NetworkList<FixedString32Bytes>();
            }
            EnsureTriggerRigidbody();
        }

        public override void OnDestroy()
        {
            _isDestroyed = true;
            if (_completedMissions != null)
            {
                _completedMissions.OnListChanged -= OnMissionsListChanged;
            }
            base.OnDestroy();
            if (Instance == this)
            {
                Instance = null;
            }
            if (_hudPanel != null && _hudPanel == s_SharedHudPanel)
            {
                s_SharedHudPanel = null;
                s_SharedTitleTMP = null;
                s_SharedDescTMP = null;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_completedMissions != null)
            {
                _completedMissions.OnListChanged -= OnMissionsListChanged;
            }
            base.OnNetworkDespawn();
        }

        private void Start()
        {
            EnsureTriggerRigidbody();
            if (!IsSpawned)
            {
                CreateUI();
            }
            UpdateMissionFlow();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && _completedMissions == null)
            {
                _completedMissions = new NetworkList<FixedString32Bytes>();
            }

            if (IsClient)
            {
                if (_hudPanel == null) CreateUI();
                if (_completedMissions != null)
                {
                    _completedMissions.OnListChanged += OnMissionsListChanged;
                    SyncLocalListWithNetwork();
                }
            }
            UpdateMissionFlow();
        }

        private void EnsureTriggerRigidbody()
        {
            var col = GetComponent<Collider>();
            if (col != null && col.isTrigger)
            {
                var rb = GetComponent<Rigidbody>();
                if (rb == null)
                {
                    rb = gameObject.AddComponent<Rigidbody>();
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
            }
        }

        public bool HasItemRequirements(MissionData mData)
        {
            if (mData == null) return false;
            bool hasInventory = mData.inventoryRequirements != null && mData.inventoryRequirements.Count > 0;
            bool hasCrafting = mData.craftingRequirements != null && mData.craftingRequirements.Count > 0;
            return hasInventory || hasCrafting;
        }

        public void InjectNewMissions(MissionData particular, List<MissionData> complementarias)
        {
            if (missions == null) missions = new List<MissionData>();

            if (complementarias != null && complementarias.Count > 0)
            {
                for (int i = complementarias.Count - 1; i >= 0; i--)
                {
                    var m = complementarias[i];
                    if (m == null) continue;
                    if (!missions.Contains(m))
                    {
                        missions.Insert(0, m);
                    }
                }
            }

            if (particular != null)
            {
                if (!missions.Contains(particular))
                {
                    missions.Insert(0, particular);
                }
            }

            UpdateMissionFlow();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null) return;

            bool isPlayer = other.CompareTag("Player") ||
                           (other.transform.root != null && other.transform.root.CompareTag("Player")) ||
                           other.GetComponentInParent<InventoryController>() != null;

            if (!isPlayer) return;

            if (Time.time - _lastTriggerTime < 0.2f) return;
            _lastTriggerTime = Time.time;

            Debug.Log($"[MissionTrigger] Jugador entró al trigger: {(missionData != null ? missionData.title : "Sin misión específica")}");

            var playerInv = other.GetComponentInParent<InventoryController>() ?? other.GetComponent<InventoryController>() ?? InventoryController.LocalInstance;
            var targetController = Instance != null ? Instance : this;

            if (this.missionData != null)
            {
                string id = GetMissionIdentifier(this.missionData);
                bool completed = IsMissionCompleted(id);

                if (!completed)
                {
                    bool hasItems = HasItemRequirements(this.missionData);
                    bool reqsMet = CheckRequirementsForPlayer(this.missionData, playerInv);

                    if (!hasItems || reqsMet)
                    {
                        if (NarrativeManager.Instance != null)
                        {
                            NarrativeManager.Instance.ExecuteMission(this.missionData, playerInv);
                        }
                        else
                        {
                            targetController.CompleteMission(this.missionData);
                        }

                        string titleToShow = !string.IsNullOrEmpty(customTitle) ? customTitle : this.missionData.title;
                        string descToShow = !string.IsNullOrEmpty(customDescription) ? customDescription : this.missionData.description;
                        targetController.ShowMessage(titleToShow, descToShow);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(customTitle) && !string.IsNullOrEmpty(customDescription))
                        {
                            targetController.ShowMessage(customTitle, customDescription);
                        }
                        else
                        {
                            targetController.ShowMissionHUD(this.missionData);
                        }
                    }
                }
            }

            targetController.InjectNewMissions(this.missionData, this.missions);
        }

        public MissionData SelectNextMissionByPriority(MissionData current, List<MissionData> stack)
        {
            if (stack == null || stack.Count == 0) return current;

            if (current != null && !IsMissionCompleted(current))
            {
                return current;
            }

            List<MissionData> uncompleted = new List<MissionData>();
            foreach (var m in stack)
            {
                if (m != null && !IsMissionCompleted(m) && !uncompleted.Contains(m))
                {
                    uncompleted.Add(m);
                }
            }

            if (uncompleted.Count == 0) return null;

            if (current != null)
            {
                foreach (var m in uncompleted)
                {
                    if (m.missionRequirements != null && m.missionRequirements.Contains(current))
                    {
                        return m;
                    }
                }
            }

            foreach (var m in uncompleted)
            {
                if (m.missionRequirements != null && m.missionRequirements.Count > 0)
                {
                    bool allReqsCompleted = true;
                    foreach (var req in m.missionRequirements)
                    {
                        if (req != null && !IsMissionCompleted(req))
                        {
                            allReqsCompleted = false;
                            break;
                        }
                    }

                    if (allReqsCompleted)
                    {
                        return m;
                    }
                }
            }

            foreach (var m in uncompleted)
            {
                if (m.missionRequirements == null || m.missionRequirements.Count == 0)
                {
                    return m;
                }
            }

            return uncompleted[0];
        }

        public void UpdateMissionFlow()
        {
            if (s_IsQuitting || _isDestroyed || !Application.isPlaying) return;

            var playerInv = InventoryController.LocalInstance;

            if (missions != null)
            {
                foreach (var m in missions)
                {
                    if (m == null) continue;

                    if (!IsMissionCompleted(m) && HasItemRequirements(m))
                    {
                        if (CheckRequirementsForPlayer(m, playerInv))
                        {
                            if (NarrativeManager.Instance != null)
                            {
                                NarrativeManager.Instance.ExecuteMission(m, playerInv);
                            }
                            else
                            {
                                CompleteMission(m);
                            }
                        }
                    }
                }
            }

            MissionData nextMission = SelectNextMissionByPriority(_currentActiveMission, missions);

            if (nextMission == null && this.missionData != null && !IsMissionCompleted(this.missionData))
            {
                nextMission = this.missionData;
            }

            if (nextMission != null)
            {
                _currentActiveMission = nextMission;
                ShowMissionHUD(nextMission);
            }
            else if (missions != null && missions.Count > 0 && AllMissionsCompleted())
            {
                ShowMessage("MISIÓN FINAL", "Has completado todas las misiones.");
            }
        }

        private bool AllMissionsCompleted()
        {
            if (missions == null || missions.Count == 0) return false;
            foreach (var m in missions)
            {
                if (m != null && !IsMissionCompleted(m)) return false;
            }
            return true;
        }

        public static int GetItemQuantityInBag(Dictionary<string, (ItemData def, int qty)> bag, ItemData reqItem)
        {
            if (bag == null || reqItem == null) return 0;

            int totalQty = 0;
            string reqName = (reqItem.itemName ?? "").Trim().ToLowerInvariant();
            int reqHash = reqItem.GetHashCode();

            foreach (var kvp in bag)
            {
                var slotDef = kvp.Value.def;
                int slotQty = kvp.Value.qty;

                if (slotDef != null)
                {
                    if (slotDef == reqItem ||
                        slotDef.GetHashCode() == reqHash ||
                        (!string.IsNullOrEmpty(slotDef.itemName) && slotDef.itemName.Trim().ToLowerInvariant() == reqName))
                    {
                        totalQty += slotQty;
                    }
                }
            }

            return totalQty;
        }

        public bool CheckRequirementsForPlayer(MissionData mData, InventoryController playerInv)
        {
            if (mData == null) return false;

            if (NarrativeManager.Instance != null)
            {
                return NarrativeManager.Instance.CheckRequirements(mData, playerInv);
            }

            var bag = playerInv != null ? playerInv.GetMyBag() : InventoryController.GetBag();

            if (mData.missionRequirements != null && mData.missionRequirements.Count > 0)
            {
                foreach (var req in mData.missionRequirements)
                {
                    if (req != null && !IsMissionCompleted(req))
                        return false;
                }
            }

            if (mData.inventoryRequirements != null && mData.inventoryRequirements.Count > 0)
            {
                foreach (var req in mData.inventoryRequirements)
                {
                    if (req.item == null) continue;
                    int requiredQty = req.amount > 0 ? req.amount : 1;
                    int playerQty = GetItemQuantityInBag(bag, req.item);
                    if (playerQty < requiredQty)
                    {
                        return false;
                    }
                }
            }

            if (mData.craftingRequirements != null && mData.craftingRequirements.Count > 0)
            {
                foreach (var trade in mData.craftingRequirements)
                {
                    if (trade == null) continue;

                    bool tradeDone = CraftingController.IsTradeCompletedAnywhere(trade);
                    if (!tradeDone)
                        return false;

                    if (trade.outputs != null && trade.outputs.Count > 0)
                    {
                        foreach (var outReq in trade.outputs)
                        {
                            if (outReq == null || outReq.item == null) continue;
                            int reqQty = outReq.GetMinRequiredAmount();
                            if (reqQty <= 0) continue;

                            int playerQty = GetItemQuantityInBag(bag, outReq.item);
                            if (playerQty < reqQty)
                                return false;
                        }
                    }
                }
            }

            return true;
        }

        private void OnMissionsListChanged(NetworkListEvent<FixedString32Bytes> changeEvent)
        {
            string id = changeEvent.Value.ToString();
            if (changeEvent.Type == NetworkListEvent<FixedString32Bytes>.EventType.Add)
            {
                if (_localCompletedMissions == null) _localCompletedMissions = new HashSet<string>();
                _localCompletedMissions.Add(id);
                if (id == _currentVisibleMissionId)
                {
                    HideMissionHUD();
                }
            }
        }

        private void SyncLocalListWithNetwork()
        {
            if (_completedMissions == null) return;
            if (_localCompletedMissions == null) _localCompletedMissions = new HashSet<string>();

            foreach (var id in _completedMissions)
            {
                _localCompletedMissions.Add(id.ToString());
            }
        }

        private void CreateUI()
        {
            if (!Application.isPlaying || s_IsQuitting || _isDestroyed) return;

            if (s_SharedHudPanel != null)
            {
                _hudPanel = s_SharedHudPanel;
                _titleTMP = s_SharedTitleTMP;
                _descTMP = s_SharedDescTMP;
                return;
            }

            GameObject canvasObj = GameObject.Find("MissionCanvas");
            if (canvasObj == null)
            {
                canvasObj = new GameObject("MissionCanvas");
                Canvas c = canvasObj.AddComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.sortingOrder = 100;
                canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasObj.AddComponent<GraphicRaycaster>();
            }

            Transform existingPanel = canvasObj.transform.Find("MissionPanel");
            if (existingPanel != null)
            {
                _hudPanel = existingPanel.gameObject;
                Transform titleChild = _hudPanel.transform.Find("Title");
                Transform descChild = _hudPanel.transform.Find("Description");

                if (titleChild != null) _titleTMP = titleChild.GetComponent<TextMeshProUGUI>();
                if (descChild != null) _descTMP = descChild.GetComponent<TextMeshProUGUI>();

                s_SharedHudPanel = _hudPanel;
                s_SharedTitleTMP = _titleTMP;
                s_SharedDescTMP = _descTMP;
                return;
            }

            _hudPanel = new GameObject("MissionPanel");
            _hudPanel.transform.SetParent(canvasObj.transform, false);

            Image panelImg = _hudPanel.AddComponent<Image>();
            panelImg.color = new Color(0, 0, 0, 0.85f);

            RectTransform rt = _hudPanel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -10);
            rt.sizeDelta = new Vector2(420, 90);

            // Título: posicionado en la parte superior del panel
            _titleTMP = CreateTextElement("Title", _hudPanel.transform, 16, Color.yellow,
                new Vector2(0f, 0.55f), new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20, 0));
            _titleTMP.fontStyle = FontStyles.Bold;

            // Descripción: posicionada en la parte inferior del panel
            _descTMP = CreateTextElement("Description", _hudPanel.transform, 13, Color.white,
                new Vector2(0f, 0f), new Vector2(1f, 0.55f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20, 0));

            s_SharedHudPanel = _hudPanel;
            s_SharedTitleTMP = _titleTMP;
            s_SharedDescTMP = _descTMP;

            _hudPanel.SetActive(false);
        }

        private TextMeshProUGUI CreateTextElement(string name, Transform parent, int size, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 delta)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = delta;

            return tmp;
        }

        private void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.mKey.wasPressedThisFrame)
            {
                if (_hudPanel != null)
                {
                    _hudPanel.SetActive(!_hudPanel.activeSelf);
                }
            }

            if (IsMenuBlockingUI())
            {
                if (_hudPanel != null && _hudPanel.activeSelf) _hudPanel.SetActive(false);
            }
        }

        private bool IsMenuBlockingUI()
        {
            if (VictoryMenu.Instance != null && GameObject.Find("VictoryMenu_Canvas") != null) return true;
            if (GameObject.Find("DefeatMenu_Canvas") != null) return true;
            return false;
        }

        public string GetMissionIdentifier(MissionData mData)
        {
            if (mData == null) return "";
            if (!string.IsNullOrEmpty(mData.title)) return mData.title;
            return mData.name;
        }

        public bool IsMissionCompleted(MissionData mData)
        {
            if (mData == null) return false;
            return IsMissionCompleted(GetMissionIdentifier(mData));
        }

        public bool IsMissionCompleted(string id)
        {
            if (string.IsNullOrEmpty(id) || _localCompletedMissions == null) return false;

            string cleanId = id.Trim().ToLowerInvariant();
            foreach (var completed in _localCompletedMissions)
            {
                string cleanCompleted = completed.Trim().ToLowerInvariant();
                if (cleanCompleted == cleanId)
                {
                    return true;
                }
            }
            return false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void CompleteMissionServerRpc(string missionId)
        {
            if (!IsMissionCompleted(missionId))
            {
                if (_completedMissions != null)
                {
                    _completedMissions.Add(missionId);
                }
            }
        }

        public void CompleteMission(MissionData mData)
        {
            if (mData != null) CompleteMission(GetMissionIdentifier(mData));
        }

        public void CompleteMission(string missionId, InventoryController inv = null)
        {
            if (string.IsNullOrEmpty(missionId)) return;

            if (_localCompletedMissions == null) _localCompletedMissions = new HashSet<string>();

            if (IsSpawned && _completedMissions != null)
            {
                CompleteMissionServerRpc(missionId);
            }
            else
            {
                if (!_localCompletedMissions.Contains(missionId))
                {
                    _localCompletedMissions.Add(missionId);
                    if (missionId == _currentVisibleMissionId) HideMissionHUD();
                }
            }
        }

        public void ShowMissionHUD(MissionData mData)
        {
            if (mData == null || s_IsQuitting || _isDestroyed || !Application.isPlaying) return;

            if (_hudPanel == null || _titleTMP == null) CreateUI();

            if (s_SharedHudPanel != null)
            {
                _hudPanel = s_SharedHudPanel;
                _titleTMP = s_SharedTitleTMP;
                _descTMP = s_SharedDescTMP;
            }

            if (_hudPanel == null) return;

            string newId = GetMissionIdentifier(mData);
            _currentVisibleMissionId = newId;

            _hudPanel.SetActive(true);
            if (_titleTMP != null) _titleTMP.text = mData.title;
            if (_descTMP != null) _descTMP.text = mData.description;
        }

        public void ShowMessage(string title, string description)
        {
            if (s_IsQuitting || _isDestroyed || !Application.isPlaying) return;

            if (_hudPanel == null) CreateUI();

            if (s_SharedHudPanel != null)
            {
                _hudPanel = s_SharedHudPanel;
                _titleTMP = s_SharedTitleTMP;
                _descTMP = s_SharedDescTMP;
            }

            if (_hudPanel == null) return;

            _hudPanel.SetActive(true);
            if (_titleTMP != null) _titleTMP.text = title;
            if (_descTMP != null) _descTMP.text = description;
        }

        public void HideMissionHUD()
        {
            if (_hudPanel != null) _hudPanel.SetActive(false);
            _currentVisibleMissionId = "";
        }
    }
}