using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using System.Collections.Generic;
using System.Linq;
using Runtime.Scripts.Player;

namespace Runtime.Scripts.Fmod
{
    public class FmodController : MonoBehaviour
    {
        public static FmodController Instance { get; private set; }

        [Header("Role Configuration")]
        [Tooltip("If checked, this object manages the FMOD audio instance.")]
        [SerializeField] private bool isMainController = true;

        [Tooltip("If checked, this object detects collisions to change alert level.")]
        [SerializeField] private bool isTrigger = false;

        [Header("Sound Settings (Main Only)")]
        [SerializeField] private EventReference eventReference;
        [SerializeField] private string alertParameterName = "Alerta";

        [Header("Alert Settings (Trigger Only)")]
        [SerializeField] private float alertLevel = 50.0f;

        private EventInstance _environmentInstance;
        private Dictionary<int, float> _activeAlerts = new Dictionary<int, float>();

        private void Awake()
        {
            if (isMainController)
            {
                if (Instance == null)
                {
                    Instance = this;
                }
                else if (Instance != this)
                {
                    Destroy(gameObject);
                    return;
                }
            }
        }

        private void Start()
        {
            if (isMainController && !eventReference.IsNull)
            {
                _environmentInstance = RuntimeManager.CreateInstance(eventReference);
                _environmentInstance.start();
                UpdateFMOD();
            }
        }

        #region Trigger Logic
        private void OnTriggerEnter(Collider other)
        {
            if (isTrigger && IsLocalPlayer(other))
            {
                if (Instance != null)
                    Instance.RegisterAlert(gameObject.GetInstanceID(), alertLevel);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (isTrigger && IsLocalPlayer(other))
            {
                if (Instance != null)
                    Instance.RemoveAlert(gameObject.GetInstanceID());
            }
        }

        private bool IsLocalPlayer(Collider other)
        {
            if (!other.CompareTag("Player")) return false;
            var pc = other.GetComponent<PlayerController>();
            return pc != null ? pc.IsOwner : true;
        }
        #endregion

        #region Alert Management (Main Only)
        public void RegisterAlert(int sourceId, float value)
        {
            if (!isMainController)
            {
                if (Instance != null) Instance.RegisterAlert(sourceId, value);
                return;
            }

            if (!_activeAlerts.ContainsKey(sourceId))
                _activeAlerts.Add(sourceId, value);
            else
                _activeAlerts[sourceId] = value;

            UpdateFMOD();
        }

        public void RemoveAlert(int sourceId)
        {
            if (!isMainController)
            {
                if (Instance != null) Instance.RemoveAlert(sourceId);
                return;
            }

            if (_activeAlerts.ContainsKey(sourceId))
            {
                _activeAlerts.Remove(sourceId);
                UpdateFMOD();
            }
        }

        private void UpdateFMOD()
        {
            if (!isMainController || !_environmentInstance.isValid()) return;

            float finalValue = _activeAlerts.Count > 0 ? _activeAlerts.Values.Max() : 0f;
            _environmentInstance.setParameterByName(alertParameterName, finalValue);
        }
        #endregion

        private void OnDestroy()
        {
            if (isMainController && _environmentInstance.isValid())
            {
                _environmentInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                _environmentInstance.release();
            }
        }
    }
}