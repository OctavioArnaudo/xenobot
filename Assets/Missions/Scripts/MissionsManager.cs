using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using Missions.Data;
using Crafting.Scripts;
using Menus.Scripts;

namespace Missions.Scripts
{
    /// <summary>
    /// Manager Único para la lógica, HUD dinámico y Triggers.
    /// </summary>
    [AddComponentMenu("Missions/Missions Manager")]
    public class MissionsManager : NetworkBehaviour
    {
        public static MissionsManager Instance { get; private set; }

        [Header("Configuración de Misiones")]
        [SerializeField] private List<MissionData> allMissions = new List<MissionData>();

        // UI Generada dinámicamente (Hardcoded)
        private GameObject _hudPanel;
        private TextMeshProUGUI _titleTMP;
        private TextMeshProUGUI _descTMP;

        // Sincronización Multiplayer
        private NetworkList<FixedString32Bytes> _completedMissions;
        private HashSet<string> _localCompletedMissions = new HashSet<string>();
        private string _currentVisibleMissionId = "";

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            _completedMissions = new NetworkList<FixedString32Bytes>();
        }

        private void Start()
        {
            // Si no hay networking o no se ha spawneado aún, creamos la UI localmente
            if (!IsSpawned)
            {
                CreateUI();
            }
        }

        public override void OnNetworkSpawn()
        {
            Debug.Log("[MissionsManager] OnNetworkSpawn called. IsClient: " + IsClient);
            if (IsClient)
            {
                // Si la UI no se creó en Start, se crea aquí
                if (_hudPanel == null) CreateUI();
                _completedMissions.OnListChanged += OnMissionsListChanged;

                // Sincronizar lista local con la de red al entrar
                SyncLocalListWithNetwork();
            }
        }

        private void OnMissionsListChanged(NetworkListEvent<FixedString32Bytes> changeEvent)
        {
            string id = changeEvent.Value.ToString();
            if (changeEvent.Type == NetworkListEvent<FixedString32Bytes>.EventType.Add)
            {
                _localCompletedMissions.Add(id);
                if (id == _currentVisibleMissionId)
                {
                    HideMissionHUD();
                }
            }
        }

        private void SyncLocalListWithNetwork()
        {
            foreach (var id in _completedMissions)
            {
                _localCompletedMissions.Add(id.ToString());
            }
        }

        private void CreateUI()
        {
            Debug.Log("[MissionsManager] Creating UI dynamically...");
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

            _hudPanel = new GameObject("MissionPanel");
            _hudPanel.transform.SetParent(canvasObj.transform, false);

            Image panelImg = _hudPanel.AddComponent<Image>();
            panelImg.color = new Color(0, 0, 0, 0.85f);

            RectTransform rt = _hudPanel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -10); // Más pegado arriba
            rt.sizeDelta = new Vector2(400, 50); // Mucho más pequeño

            // Título compacto
            _titleTMP = CreateTextElement("Title", _hudPanel.transform, 16, Color.yellow, new Vector2(0, 10), new Vector2(-10, -25));
            _titleTMP.fontStyle = FontStyles.Bold;

            // Descripción compacta
            _descTMP = CreateTextElement("Description", _hudPanel.transform, 12, Color.white, new Vector2(0, -10), new Vector2(-10, -25));

            _hudPanel.SetActive(false);
        }

        private TextMeshProUGUI CreateTextElement(string name, Transform parent, int size, Color color, Vector2 pos, Vector2 delta)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.anchoredPosition = pos;
            rt.sizeDelta = delta;

            return tmp;
        }

        private void Update()
        {
            // Toggle HUD with M key
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.mKey.wasPressedThisFrame)
            {
                if (_hudPanel != null)
                {
                    _hudPanel.SetActive(!_hudPanel.activeSelf);
                }
            }

            // If victory or defeat menus are active, hide mission HUD and stop processing
            if (IsMenuBlockingUI())
            {
                if (_hudPanel != null && _hudPanel.activeSelf) _hudPanel.SetActive(false);
                return;
            }

            // Periodic inventory check (every 0.5s to save CPU)
            if (Time.time % 0.5f < Time.deltaTime)
            {
                UpdateMissionFlow();
            }
        }

        private bool IsMenuBlockingUI()
        {
            // Check Victory Menu
            if (VictoryMenu.Instance != null)
            {
                // Accessing private field via reflection or checking if canvas exists
                // Since I can't change VictoryMenu easily, I'll check if the canvas name exists
                if (GameObject.Find("VictoryMenu_Canvas") != null) return true;
            }

            // Check Defeat Menu
            if (GameObject.Find("DefeatMenu_Canvas") != null) return true;

            return false;
        }

        private void UpdateMissionFlow()
        {
            var bag = InventoryController.GetBag();
            MissionData nextMission = null;

            // Iterate through missions in the list order
            foreach (var mission in allMissions)
            {
                if (mission == null) continue;

                // Check if this specific mission's requirements are met by inventory
                bool requirementsMet = true;

                // 1. Check Gathering Requirements
                if (mission.gatheringRequirements != null)
                {
                    foreach (var req in mission.gatheringRequirements)
                    {
                        if (req.item == null) continue;
                        string key = req.item.itemCode.ToLowerInvariant();
                        if (!bag.TryGetValue(key, out var slot) || slot.qty < req.amount)
                        {
                            requirementsMet = false;
                            break;
                        }
                    }
                }

                if (!requirementsMet)
                {
                    // This is the first mission in the list whose requirements are NOT met.
                    // Therefore, this is the current active objective.
                    nextMission = mission;
                    break;
                }
                else
                {
                    // If requirements ARE met, this mission is considered "Completed" in the flow.
                    // We continue to the next one.
                }
            }

            if (nextMission != null)
            {
                if (_currentVisibleMissionId != nextMission.missionId)
                {
                    ShowMissionHUD(nextMission);
                }
            }
            else if (allMissions.Count > 0)
            {
                // All missions in the list are satisfied
                ShowMessage("MISIÓN FINAL", "Has recolectado todo. ¡Busca la salida!");
            }
        }

        public void ShowMissionHUD(MissionData mission)
        {
            if (mission == null) return;

            if (_hudPanel == null) CreateUI();
            if (_hudPanel == null) return;

            _currentVisibleMissionId = mission.missionId;
            _hudPanel.SetActive(true);
            if (_titleTMP != null) _titleTMP.text = mission.title;
            if (_descTMP != null) _descTMP.text = mission.description;
        }

        public void ShowMessage(string title, string description)
        {
            if (_hudPanel == null) CreateUI();
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
