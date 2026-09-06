using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine.SceneManagement;
using NGO.Networking;
using Crafting.Scripts;
using Combating.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for character Identity visuals (Name Tags, Aureoles).
    /// Uses identity data stored in the Hub (ModularController).
    /// </summary>
    public class GuiController : MonoBehaviour, IModular
    {
        public static GuiController Instance { get; private set; }

        [Header("Identity Visuals")]
        public TMPro.TMP_Text nameTagText;

        [Header("Visibility")]
        public System.Collections.Generic.List<string> allowedScenes = new System.Collections.Generic.List<string> { "BiomaScene" };

        private ModularController _hub;
        private Camera _mainCamCache;

        private Transform _aureoleRoot;
        private Vector3 _aureoleBaseOffset = new Vector3(0, 2.4f, 0);

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            // Hub will call Bind() manually during assembly.
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
            if (_hub == null) ResolveReferences();
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);

                // If offline, initialize identity immediately in the hub
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                {
                    SetInitialIdentityInHub();
                }

                OnRefreshModule();
            }
        }

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                // Subscribe to Hub identity changes
                _hub.playerName.OnValueChanged -= OnNameChanged;
                _hub.playerName.OnValueChanged += OnNameChanged;
                _hub.playerColor.OnValueChanged -= OnColorChanged;
                _hub.playerColor.OnValueChanged += OnColorChanged;

                UpdateVisuals();
            }
        }

        private void OnNameChanged(FixedString32Bytes old, FixedString32Bytes newVal) => UpdateVisuals();
        private void OnColorChanged(Color old, Color newVal) => UpdateVisuals();

        private void ResolveReferences()
        {
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
        }

        private void SetInitialIdentityInHub()
        {
            // Identity assignment moved to PlayerController.OnNetworkSpawn to ensure NetworkVariable safety
        }

        void Update()
        {
            if (nameTagText != null)
            {
                if (_mainCamCache == null) _mainCamCache = Camera.main;
                if (_mainCamCache != null)
                {
                    nameTagText.transform.rotation = Quaternion.LookRotation(nameTagText.transform.position - _mainCamCache.transform.position);
                }
            }

            if (_aureoleRoot != null && _aureoleRoot.gameObject.activeSelf)
            {
                float bob = Mathf.Sin(Time.time * 2f) * 0.1f;
                _aureoleRoot.localPosition = _aureoleBaseOffset + Vector3.up * bob;
            }
        }

        public void UpdateVisuals()
        {
            ValidateVisualComponents();
            if (_aureoleRoot != null) _aureoleRoot.gameObject.SetActive(IsNetworkActive);

            if (nameTagText != null && _hub != null)
            {
                nameTagText.text = _hub.playerName.Value.ToString();
                nameTagText.color = _hub.playerColor.Value;
            }
        }

        private void ValidateVisualComponents()
        {
            if (_aureoleRoot == null)
            {
                var existingRoot = transform.Find("AureoleRoot");
                if (existingRoot != null) _aureoleRoot = existingRoot;
                else
                {
                    _aureoleRoot = new GameObject("AureoleRoot").transform;
                    Animator anim = (_hub != null) ? _hub.animator : GetComponentInChildren<Animator>();
                    Transform headBone = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
                    _aureoleRoot.SetParent(headBone != null ? headBone : transform);
                    _aureoleRoot.localPosition = headBone != null ? new Vector3(0, 0.4f, 0) : _aureoleBaseOffset;
                }
            }

            if (nameTagText == null)
            {
                nameTagText = _aureoleRoot.GetComponentInChildren<TMPro.TMP_Text>();
            }
        }
    }
}
