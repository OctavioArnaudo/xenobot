using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using Missions.Data;

namespace Missions.Manager
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

        public void CheckLocation(string location)
        {
            Debug.Log("[MissionsManager] Checking location: " + location);

            // Permitir funcionar en local si no hay networking activo
            bool isLocalOnly = !IsSpawned;

            foreach (var m in allMissions)
            {
                if (m == null) continue;
                if (IsMissionCompleted(m.missionId)) continue;

                if (m.requiredLocation == location)
                {
                    if (AreRequirementsMet(m))
                    {
                        ShowMissionHUD(m);
                    }
                }
            }
        }

        public bool IsMissionCompleted(string id)
        {
            return _localCompletedMissions.Contains(id);
        }

        private bool AreRequirementsMet(MissionData mission)
        {
            if (mission.requiredMissionIds == null || mission.requiredMissionIds.Count == 0) return true;
            foreach (var reqId in mission.requiredMissionIds)
            {
                if (!IsMissionCompleted(reqId)) return false;
            }
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void CompleteMissionServerRpc(string missionId)
        {
            if (!IsMissionCompleted(missionId))
            {
                _completedMissions.Add(missionId);
            }
        }

        /// <summary>
        /// Versión híbrida para completar misiones (Local o Red).
        /// </summary>
        public void CompleteMission(string missionId)
        {
            if (IsSpawned)
            {
                CompleteMissionServerRpc(missionId);
            }
            else
            {
                // Fallback Local
                if (!_localCompletedMissions.Contains(missionId))
                {
                    _localCompletedMissions.Add(missionId);
                    if (missionId == _currentVisibleMissionId) HideMissionHUD();
                    Debug.Log("[MissionsManager] Mission completed LOCALLY: " + missionId);
                }
            }
        }

        private void ShowMissionHUD(MissionData mission)
        {
            Debug.Log("[MissionsManager] Showing HUD for mission: " + mission.title);
            if (_hudPanel == null) { Debug.LogError("[MissionsManager] _hudPanel is NULL! UI creation might have failed."); return; }
            _currentVisibleMissionId = mission.missionId;
            _hudPanel.SetActive(true);
            _titleTMP.text = mission.title;
            _descTMP.text = mission.description;
        }

        private void HideMissionHUD()
        {
            if (_hudPanel != null) _hudPanel.SetActive(false);
            _currentVisibleMissionId = "";
        }
    }
}
