using UnityEngine;
using Runtime.Scripts.Gate;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Unity.Collections;
using TMPro;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Runtime.Scripts.Singleton;
using Runtime.Scripts.Health;
using Runtime.Scripts.Shoot;
using Runtime.Scripts.Propulsion;
using Runtime.Scripts.Shield;
using Runtime.Scripts.Player;
using Runtime.Scripts.Levels;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Settings;
using Runtime.Scripts.Inventory;
using Runtime.Scripts.Crafting;
using Runtime.Scripts.Narrative;
using static Runtime.Scripts.Gate.MenuGate;

namespace Runtime.Scripts.Hud
{
    /// <summary>
    /// Unified controller for character progression, HUD and Identity.
    /// Optimized to reduce CPU overhead and audio starvation.
    /// </summary>
    public class HudController : CSingletonNB<HudController>
    {

        [Header("Identity & Visuals")]
        public NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(new FixedString32Bytes(""), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<Color> playerColor = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public TMPro.TMP_Text nameTagText;

        [Header("Initial Ranges")]
        public Vector2 attackRange = new Vector2(5f, 15f);
        public Vector2 defenseRange = new Vector2(3f, 10f);

        [Header("Base Growth")]
        public float attackPerLevel = 2f;
        public float defensePerLevel = 1.5f;
        public float expToLevelUp = 100f;

        // Use NetworkVariables for synchronization
        public NetworkVariable<float> NetAttack = new NetworkVariable<float>(10, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> NetDefense = new NetworkVariable<float>(5, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> NetLevel = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> NetExp = new NetworkVariable<float>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> NetExpToLevelUp = new NetworkVariable<float>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public float Attack => IsNetworkActive ? NetAttack.Value : m_OfflineAttack;
        public float Defense => IsNetworkActive ? NetDefense.Value : m_OfflineDefense;
        public int Level => IsNetworkActive ? NetLevel.Value : m_OfflineLevel;
        public float Exp => IsNetworkActive ? NetExp.Value : m_OfflineExp;
        public float ExpToLevelUp => IsNetworkActive ? NetExpToLevelUp.Value : m_OfflineExpToLevelUp;

        private float m_OfflineAttack = 10, m_OfflineDefense = 5, m_OfflineExp = 0, m_OfflineExpToLevelUp = 100;
        private int m_OfflineLevel = 1;


        private HealthController m_PlayerHealth;
        private PropulsionController _propulsion;
        private ShootController _shooter;
        private ShieldController _shield;
        private PlayerController _player;

        private float _lastTimeUpdate = -1f;
        private bool _inBioma;
        private string _cachedTimeStr = "00:00";
        private Camera _mainCamCache;

        private PropulsionController GetPropulsion()
        {
            if (_propulsion == null)
            {
                _propulsion = GetComponent<PropulsionController>() ??
                              GetComponentInParent<PropulsionController>() ??
                              GetComponentInChildren<PropulsionController>();
            }
            return _propulsion;
        }

        private ShootController GetShooter()
        {
            if (_shooter == null)
            {
                _shooter = GetComponent<ShootController>() ??
                           GetComponentInParent<ShootController>() ??
                           GetComponentInChildren<ShootController>();
            }
            return _shooter;
        }

        private ShieldController GetShield()
        {
            if (_shield == null)
            {
                _shield = GetComponent<ShieldController>() ??
                          GetComponentInParent<ShieldController>() ??
                          GetComponentInChildren<ShieldController>();
            }
            return _shield;
        }

        private PlayerController GetPlayer()
        {
            if (_player == null)
            {
                _player = GetComponent<PlayerController>() ??
                          GetComponentInParent<PlayerController>() ??
                          GetComponentInChildren<PlayerController>();
            }
            return _player;
        }

        private System.Collections.Generic.List<string> GetDynamicDefenseKeys()
        {
            var keys = _kd; keys.Clear();
            var pc = GetPlayer();
            var prop = GetPropulsion();

            bool isMoving = pc != null && pc.move.sqrMagnitude > 0.01f;
            bool isSprinting = pc != null && pc.sprint;
            bool isGrounded = pc == null || pc.Grounded;
            bool isFlying = prop != null && prop.IsFlying;
            bool hasJet = prop != null && prop.MaxJetpack > 0;

            if (!isMoving) keys.Add("WASD: Walk");
            if (isMoving && !isSprinting) keys.Add("Shift: Sprint");
            if (isGrounded) keys.Add("Space: Jump");
            if (hasJet && !isFlying) keys.Add("Space/B: Fly");

            return keys;
        }

        private System.Collections.Generic.List<string> GetDynamicAttackKeys()
        {
            var keys = _ka; keys.Clear();
            var pc = GetPlayer();
            var shooter = GetShooter();
            var shield = GetShield();

            bool isFiring = pc != null && (pc.fire || pc.fireHeld);
            bool isShieldActive = shield != null && shield.IsShieldActive;
            bool isReloading = shooter != null && shooter.isReloading;
            bool needsAmmo = shooter != null && shooter.currentAmmo < shooter.EffectiveMaxAmmo;

            if (!isFiring) keys.Add("LMB: Shoot");
            if (shield != null && !isShieldActive) keys.Add("MMB: Shield");
            if (shooter != null && !isReloading && needsAmmo) keys.Add("R: Reload");

            return keys;
        }

        private Transform _aureoleRoot;
        private Vector3 _aureoleBaseOffset = new Vector3(0, 2.4f, 0);

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;

        void Update()
        {
            UpdateHud();

            // Billboard effect para el NameTag en red (Cacheando la camara para evitar Starvation)
            if (nameTagText != null)
            {
                if (_mainCamCache == null) _mainCamCache = Camera.main;
                if (_mainCamCache != null)
                {
                    nameTagText.transform.rotation = Quaternion.LookRotation(nameTagText.transform.position - _mainCamCache.transform.position);
                }
            }

            // Aureole Animations: Floating & Rotation
            if (_aureoleRoot != null && _aureoleRoot.gameObject.activeSelf)
            {
                float bob = Mathf.Sin(Time.time * 2f) * 0.1f;
                _aureoleRoot.localPosition = _aureoleBaseOffset + Vector3.up * bob;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                InitializeStats();

                // Inicialización offline para HUD y Visuales
                m_PlayerHealth = GetComponent<HealthController>();
                UpdateVisuals();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_PlayerHealth = GetComponent<HealthController>();

            // SERVER: Initialize stats for EVERY player spawned
            if (IsServer) InitializeStats();

            if (IsOwner)
            {
                string baseName = PlayerPrefs.GetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.UserName), "Player");
                if (string.IsNullOrEmpty(baseName)) baseName = "Player";

                playerName.Value = baseName;
                playerColor.Value = Color.white;
            }
            playerName.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
            playerColor.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
            UpdateVisuals();
        }

        void InitializeStats()
        {
            float atk = Random.Range(attackRange.x, attackRange.y);
            float def = Random.Range(defenseRange.x, defenseRange.y);

            if (IsNetworkActive)
            {
                if (IsServer)
                {
                    NetAttack.Value = atk;
                    NetDefense.Value = def;
                    NetLevel.Value = 1;
                    NetExp.Value = 0;
                    NetExpToLevelUp.Value = 100f;
                }
            }
            else
            {
                m_OfflineAttack = atk;
                m_OfflineDefense = def;
                m_OfflineLevel = 1;
                m_OfflineExp = 0;
                m_OfflineExpToLevelUp = 100f;
            }
        }

        public void UpdateVisuals()
        {
            ValidateVisualComponents();
            // Allow aureole in both online and offline mode for testing/single player
            if (_aureoleRoot != null) _aureoleRoot.gameObject.SetActive(true);

            string dName = "";

            if (IsNetworkActive)
            {
                dName = playerName.Value.ToString();
            }
            else
            {
                // Offline/Local mode: Try config first, then default
                dName = PlayerPrefs.GetString(SettingsMenu.PrefKeys.GetKey(SettingsMenu.PrefKeys.Key.UserName), "Player");
            }

            if (string.IsNullOrEmpty(dName)) dName = "PLAYER";

            if (nameTagText != null) nameTagText.text = dName;
        }

        private void ValidateVisualComponents()
        {
            // Zero-Dependency Bootstrapping: Root de la Aureola
            if (_aureoleRoot == null)
            {
                var existingRoot = transform.Find("AureoleRoot") ?? transform.GetComponentInChildren<Animator>()?.transform.Find("AureoleRoot");

                if (existingRoot != null)
                {
                    _aureoleRoot = existingRoot;
                }
                else
                {
                    _aureoleRoot = new GameObject("AureoleRoot").transform;

                    // Intentar encontrar el hueso de la cabeza para que la siga fielmente
                    Animator anim = GetComponentInChildren<Animator>();
                    Transform headBone = null;
                    if (anim != null && anim.isHuman) headBone = anim.GetBoneTransform(HumanBodyBones.Head);
                    if (headBone == null)
                    {
                        foreach (Transform child in GetComponentsInChildren<Transform>())
                        {
                            if (child.name.ToLower().Contains("head"))
                            {
                                headBone = child;
                                break;
                            }
                        }
                    }

                    _aureoleRoot.SetParent(headBone != null ? headBone : transform);
                    _aureoleRoot.localPosition = headBone != null ? new Vector3(0, 0.4f, 0) : _aureoleBaseOffset;
                }
            }

            // Zero-Dependency Bootstrapping: Fallback para el NameTag
            if (nameTagText == null)
            {
                nameTagText = _aureoleRoot.GetComponentInChildren<TMPro.TMP_Text>();
                if (nameTagText == null)
                {
                    GameObject tagGO = new GameObject("NameTag");
                    tagGO.transform.SetParent(_aureoleRoot);
                    tagGO.transform.localPosition = new Vector3(0, 0.4f, 0); // Above the halo
                    var tmp = tagGO.AddComponent<TMPro.TextMeshPro>();
                    tmp.alignment = TMPro.TextAlignmentOptions.Center;
                    tmp.fontSize = 4;
                    tmp.rectTransform.sizeDelta = new Vector2(5, 1);
                    nameTagText = tmp;
                }
            }
        }

        public void AddExp(float amount)
        {
            if (IsNetworkActive)
            {
                if (IsServer) InternalAddExp(amount);
                else AddExpServerRpc(amount);
            }
            else
            {
                InternalAddExp(amount);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void AddExpServerRpc(float amount) => InternalAddExp(amount);

        private void InternalAddExp(float amount)
        {
            if (IsNetworkActive)
            {
                NetExp.Value += amount;
                while (NetExp.Value >= NetExpToLevelUp.Value)
                {
                    NetExp.Value -= NetExpToLevelUp.Value;
                    LevelUp();
                }
            }
            else
            {
                m_OfflineExp += amount;
                while (m_OfflineExp >= m_OfflineExpToLevelUp)
                {
                    m_OfflineExp -= m_OfflineExpToLevelUp;
                    LevelUp();
                }
            }
        }
        public float EffectiveAttackPerLevel
        {
            get
            {
                if (BalanceManager.Instance != null)
                {
                    var stats = BalanceManager.Instance.GetEntityBalance(gameObject);
                    if (stats.attackPerLevel.use && stats.attackPerLevel.value > 0) return stats.attackPerLevel.value;
                }
                return attackPerLevel;
            }
        }

        public float EffectiveDefensePerLevel
        {
            get
            {
                if (BalanceManager.Instance != null)
                {
                    var stats = BalanceManager.Instance.GetEntityBalance(gameObject);
                    if (stats.defensePerLevel.use && stats.defensePerLevel.value > 0) return stats.defensePerLevel.value;
                }
                return defensePerLevel;
            }
        }

        void LevelUp()
        {
            if (IsNetworkActive)
            {
                NetLevel.Value++;
                NetAttack.Value += EffectiveAttackPerLevel;
                NetDefense.Value += EffectiveDefensePerLevel;
                NetExpToLevelUp.Value *= 1.2f;
            }
            else
            {
                m_OfflineLevel++;
                m_OfflineAttack += EffectiveAttackPerLevel;
                m_OfflineDefense += EffectiveDefensePerLevel;
                m_OfflineExpToLevelUp *= 1.2f;
            }

            // Bono de Vida y Jetpack al subir de nivel
            if (m_PlayerHealth != null)
            {
                m_PlayerHealth.UpgradeMaxStats(15);
            }
            var prop = GetPropulsion();
            if (prop != null)
            {
                prop.UpgradeMaxFuel(20f);
            }
        }

        // ---------------- HUD (uGUI) ----------------
        // Tablero de instrumentos metalico con 4 lamparas de vidrio. Se construye una sola vez; los valores se refrescan 10 veces por segundo.
        static readonly Color[] BarCol =
        {
            new Color(1f, 0.15f, 0.1f), new Color(0.1f, 0.9f, 1f), new Color(1f, 0.8f, 0.1f), new Color(0.7f, 0.3f, 1f)
        };
        static readonly string[] BarName = { "HP", "JET", "AMMO", "SHIELD" };

        GameObject _uiRoot, _statsPanel;
        readonly RectTransform[] _fill = new RectTransform[4];
        readonly TextMeshProUGUI[] _barTxt = new TextMeshProUGUI[4];
        readonly float[] _lastF = new float[4];
        TextMeshProUGUI _keysL, _keysR, _statsTxt;
        Image _plate;
        Image _scopeRing;
        static Sprite s_scope, s_ring;
        float _uiTick;
        readonly System.Collections.Generic.List<string> _kd = new System.Collections.Generic.List<string>(4);
        readonly System.Collections.Generic.List<string> _ka = new System.Collections.Generic.List<string>(4);

        // Botones de panel (metalicos, punto rubi). Stats se abre aqui; el resto notifica por evento.
        static readonly string[] PanelKey = { "inventory", "craft", "stats", "missions" };
        static readonly string[] PanelLbl = { "INV", "CRAFT", "K", "J" };
        static readonly Color Ruby = new Color(0.85f, 0.04f, 0.14f);

        void OpenPanel(string k)
        {
            switch (k)
            {
                case "inventory": InventoryController.Toggle(); break;
                case "craft": CraftingController.Toggle(); break;
                case "stats": SetStats(!_statsPanel.activeSelf); break;
                default: if (StoryManager.Instance != null) StoryManager.Instance.ToggleMissions(); break; // J lo gestiona StoryManager
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (_uiRoot != null) Destroy(_uiRoot);
        }

        void UpdateHud()
        {
            if (!CanExecuteLocalLogic) return;

            // Tiempo de sesion (lo usan las pantallas de victoria y derrota): cada 0,5 s
            if (Time.time - _lastTimeUpdate > 0.5f)
            {
                _lastTimeUpdate = Time.time;
                _inBioma = SceneManager.GetActiveScene().name == "BiomaScene";
                if (_inBioma)
                {
                    float t = Time.timeSinceLevelLoad;
                    LevelsMenu.lastSessionTime = t;
                    LevelsMenu.lastSessionLevel = SceneManager.GetActiveScene().name;
                    _cachedTimeStr = LevelsMenu.FormatTime(t);
                }
            }
            if (!_inBioma || m_PlayerHealth == null) return;
            if (_uiRoot == null) BuildHud();

            if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
            {
                SetStats(!_statsPanel.activeSelf);
            }

            if (Time.unscaledTime < _uiTick) return;
            _uiTick = Time.unscaledTime + 0.1f;
            RefreshBars();
            _plate.raycastTarget = MenuGate.Busy; // el icono de mira solo es clicable con un menu abierto
            var pcs = GetPlayer();
            if (pcs != null && _scopeRing.gameObject.activeSelf != pcs.scopeMode) _scopeRing.gameObject.SetActive(pcs.scopeMode);
            if (_statsPanel.activeSelf) RefreshStats();
        }

        void OnGate(string k) { if (k == "stats" && _statsPanel != null && _statsPanel.activeSelf) SetStats(false); }

        // Un solo menu a la vez; Esc lo cierra (MenuGate). Cursor libre = jugador bloqueado.
        void SetStats(bool open)
        {
            if (open && !MenuGate.TryOpen("stats")) return;
            MenuGate.CloseRequested -= OnGate;
            if (open) MenuGate.CloseRequested += OnGate; else MenuGate.Close("stats");
            _statsPanel.SetActive(open);
            bool free = open || MenuGate.Busy;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
            RefreshStats();
        }

        void BuildHud()
        {
            _uiRoot = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var c = _uiRoot.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 90;
            var cs = _uiRoot.GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080);

            // Tablero inferior: HP, JET, AMMO, SHIELD
            var dash = Dash(_uiRoot.transform);
            var bars = Img(dash, Color.clear);
            bars.anchorMin = Vector2.zero;
            bars.anchorMax = new Vector2(1f, 0f);
            bars.pivot = new Vector2(0.5f, 0f);
            bars.sizeDelta = new Vector2(0f, 104f);
            bars.anchoredPosition = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                var cell = Img(bars, Color.clear);
                Fit(cell, i * 0.25f, 0f, (i + 1) * 0.25f, 1f);
                var lab = Txt(cell, 20, Amber, TextAlignmentOptions.TopLeft, FontStyles.Bold);
                lab.text = BarName[i];
                Fit(lab.rectTransform, 0f, 1f, 1f, 1f);
                lab.rectTransform.offsetMin = new Vector2(24f, -36f);
                lab.rectTransform.offsetMax = new Vector2(-24f, -8f);
                var slot = Lamp(cell, BarCol[i], out _fill[i]);
                Fit(slot, 0f, 0f, 1f, 1f);
                slot.offsetMin = new Vector2(24f, 14f);
                slot.offsetMax = new Vector2(-24f, -40f);
                var t = Txt(slot, 28, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                t.outlineWidth = 0.25f;
                t.outlineColor = Color.black;
                Fit(t.rectTransform, 0f, 0f, 1f, 1f);
                _barTxt[i] = t;
            }

            // Teclas disponibles, sobre el tablero (izquierda: movimiento, derecha: combate)
            var keyCol = new Color(0.95f, 0.95f, 0.5f);
            _keysL = Txt(_uiRoot.transform, 22, keyCol, TextAlignmentOptions.BottomLeft, FontStyles.Bold);
            var r = _keysL.rectTransform;
            r.anchorMin = new Vector2(0.29f, 0f); r.anchorMax = new Vector2(0.5f, 0f); r.pivot = new Vector2(0f, 0f);
            r.sizeDelta = new Vector2(0f, 30f); r.anchoredPosition = new Vector2(0f, 112f);
            _keysR = Txt(_uiRoot.transform, 22, keyCol, TextAlignmentOptions.BottomRight, FontStyles.Bold);
            r = _keysR.rectTransform;
            r.anchorMin = new Vector2(0.5f, 0f); r.anchorMax = new Vector2(0.71f, 0f); r.pivot = new Vector2(1f, 0f);
            r.sizeDelta = new Vector2(0f, 30f); r.anchoredPosition = new Vector2(0f, 112f);

            // Mira telescopica (tecla 1): placa metalica; el aro verde indica que esta activa. Clic = alternar (solo con un menu abierto).
            r = Rnd(Img(_uiRoot.transform, new Color(0.02f, 0.03f, 0.03f)));
            _plate = r.GetComponent<Image>();
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(1f, 0f);
            r.sizeDelta = new Vector2(80f, 80f);
            r.anchoredPosition = new Vector2(-30f, 122f);
            var btn = _plate.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { var p = GetPlayer(); if (p != null) p.scopeMode = !p.scopeMode; });
            var ring = Img(r, Green);
            _scopeRing = ring.GetComponent<Image>();
            _scopeRing.sprite = RingSprite();
            Mid(ring, 70f);
            ring.gameObject.SetActive(false);
            var ic = Img(r, new Color(0.8f, 0.9f, 0.8f));
            ic.GetComponent<Image>().sprite = ScopeSprite();
            Mid(ic, 48f);

            // Indicador de la tecla 1 junto al icono de mira
            var one = Txt(r, 22, Amber, TextAlignmentOptions.Center, FontStyles.Bold);
            one.text = "1";
            one.raycastTarget = false;
            one.rectTransform.anchorMin = one.rectTransform.anchorMax = one.rectTransform.pivot = new Vector2(0f, 1f);
            one.rectTransform.sizeDelta = new Vector2(24f, 24f);
            one.rectTransform.anchoredPosition = new Vector2(4f, -2f);

            // 4 botones metalicos redondos con punto rubi: inventario, crafteo, stats (K), misiones (J)
            for (int i = 0; i < 4; i++)
            {
                string key = PanelKey[i];
                var b = Rnd(Img(_uiRoot.transform, new Color(0.62f, 0.64f, 0.66f)));
                b.anchorMin = b.anchorMax = b.pivot = new Vector2(1f, 0f);
                b.sizeDelta = new Vector2(56f, 56f);
                b.anchoredPosition = new Vector2(-126f - (3 - i) * 68f, 134f);
                var bt = b.gameObject.AddComponent<Button>();
                bt.transition = Selectable.Transition.None;
                bt.onClick.AddListener(() => OpenPanel(key));
                Mid(Rnd(Img(b, new Color(0.3f, 0.32f, 0.34f))), 44f);
                Mid(Rnd(Img(b, Ruby)), 16f);
                var l = Txt(b, 16, Amber, TextAlignmentOptions.Center, FontStyles.Bold);
                l.text = PanelLbl[i];
                l.raycastTarget = false;
                Fit(l.rectTransform, 0f, 1f, 1f, 1f);
                l.rectTransform.offsetMin = new Vector2(0f, 2f);
                l.rectTransform.offsetMax = new Vector2(0f, 22f);
            }

            // Panel de stats (K): visor
            _statsPanel = Term(_uiRoot.transform, new Vector2(760f, 420f), "STATS", out _statsTxt).gameObject;
            _statsTxt.fontSize = 36;
            _statsPanel.SetActive(false);
        }

        void RefreshBars()
        {
            var prop = GetPropulsion();
            var shooter = GetShooter();
            var shield = GetShield();

            Bar(0, m_PlayerHealth.EffectiveCurrentHealth, m_PlayerHealth.EffectiveMaxHealth,
                $"{m_PlayerHealth.EffectiveCurrentHealth}/{m_PlayerHealth.EffectiveMaxHealth}");

            if (prop != null && prop.MaxJetpack > 0) Bar(1, prop.JetpackFuel, prop.MaxJetpack, $"{prop.JetpackFuel:F0}/{prop.MaxJetpack:F0}");
            else Bar(1, 0f, 1f, "N/A");

            if (shooter != null)
                Bar(2, shooter.currentAmmo, shooter.EffectiveMaxAmmo,
                    shooter.isReloading ? "RELOADING" : $"{shooter.currentAmmo}/{shooter.EffectiveMaxAmmo}");
            else Bar(2, 0f, 1f, "NO WEAPON");

            if (shield != null)
            {
                float val = shield.CurrentShieldHealth;
                float max = shield.EffectiveMaxShieldHealth > 0 ? shield.EffectiveMaxShieldHealth : 100f;
                string str = $"{val:F0}/{max:F0}";
                if (shield.InCooldown)
                {
                    val = shield.CooldownProgress * max;
                    str = $"COOLDOWN {shield.CooldownRemaining:F1}s";
                }
                else if (!shield.isUnlocked)
                {
                    val = 0f;
                    str = "LOCKED";
                }
                Bar(3, val, max, str);
            }
            else Bar(3, 0f, 1f, "N/A");

            _keysL.text = string.Join("   ", GetDynamicDefenseKeys());
            _keysR.text = string.Join("   ", GetDynamicAttackKeys());
        }

        void Bar(int i, float val, float max, string str)
        {
            float f = Mathf.Clamp01(val / (max > 0f ? max : 1f));
            if (!Mathf.Approximately(f, _lastF[i]))
            {
                _lastF[i] = f;
                _fill[i].anchorMax = new Vector2(f, 1f);
            }
            _barTxt[i].text = str;
        }

        void RefreshStats()
        {
            _statsTxt.text = $"TIME<pos=45%>{_cachedTimeStr}\nATK<pos=45%>{Attack:F0}\nDEF<pos=45%>{Defense:F0}\nLVL<pos=45%>{Level}\nEXP<pos=45%>{Exp:F0} / {ExpToLevelUp:F0}";
        }

        // Sprites dibujados por codigo, una sola vez y compartidos.
        static Sprite ScopeSprite()
        {
            if (s_scope != null) return s_scope;
            return s_scope = Draw((x, y) =>
            {
                float dx = Mathf.Abs(x - 31.5f), dy = Mathf.Abs(y - 31.5f), d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Band(d, 17f, 21f);
                a = Mathf.Max(a, Mathf.Clamp01(2f - dx) * Band(dy, 6f, 28f));
                a = Mathf.Max(a, Mathf.Clamp01(2f - dy) * Band(dx, 6f, 28f));
                return Mathf.Max(a, Mathf.Clamp01(2.5f - d));
            });
        }

        static Sprite RingSprite()
        {
            if (s_ring != null) return s_ring;
            return s_ring = Draw((x, y) =>
            {
                float dx = x - 31.5f, dy = y - 31.5f;
                return Band(Mathf.Sqrt(dx * dx + dy * dy), 27f, 31.5f);
            });
        }

        static float Band(float v, float a, float b) => Mathf.Clamp01(Mathf.Min(v - a, b - v) + 0.5f);

        static Sprite Draw(System.Func<int, int, float> alpha)
        {
            const int N = 64;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255f));
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}