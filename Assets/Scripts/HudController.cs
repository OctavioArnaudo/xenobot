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

        GameObject _uiRoot;
        readonly RectTransform[] _fill = new RectTransform[4];
        readonly TextMeshProUGUI[] _barTxt = new TextMeshProUGUI[4];
        readonly float[] _lastF = new float[4];
        readonly Image[] _plate = new Image[3], _ring = new Image[3]; // 0 melee, 1 disparo, 2 mira
        Image _glow, _jDot; // aviso rojo del boton J: historia nueva sin leer
        bool _lit;
        static Sprite s_scope, s_ring, s_glow, s_fist, s_gun;
        float _uiTick;

        // Botones de panel (metalicos, punto rubi): TAB = inventario / personaje, J = misiones + radar.
        static readonly string[] PanelLbl = { "TAB", "J" };
        static readonly Color Ruby = new Color(0.85f, 0.04f, 0.14f);

        void OpenPanel(int i)
        {
            if (i == 0) InventoryController.Toggle();
            else if (StoryManager.Instance != null) StoryManager.Instance.ToggleMissions();
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

            if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame) InventoryController.Toggle();

            // Boton J: brilla fuerte en rojo mientras haya historia nueva sin revisar (empieza encendido).
            var story = StoryManager.Instance;
            bool lit = story != null && story.Unread;
            if (lit != _lit)
            {
                _lit = lit;
                _glow.gameObject.SetActive(lit);
                if (!lit) { _jDot.color = Ruby; _jDot.rectTransform.localScale = Vector3.one; }
            }
            if (_lit)
            {
                float p = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                _glow.color = new Color(1f, 0.05f, 0.03f, 0.6f + 0.4f * p);
                _glow.rectTransform.localScale = Vector3.one * (0.9f + 0.35f * p);
                _jDot.color = Color.Lerp(new Color(1f, 0.2f, 0.15f), new Color(1f, 0.9f, 0.85f), p);
                _jDot.rectTransform.localScale = Vector3.one * (1.1f + 0.5f * p);
            }

            if (Time.unscaledTime < _uiTick) return;
            _uiTick = Time.unscaledTime + 0.1f;
            RefreshBars();
            bool busy = MenuGate.Busy; // los iconos solo son clicables con un menu abierto
            var pcs = GetPlayer();
            for (int i = 0; i < 3; i++)
            {
                _plate[i].raycastTarget = busy;
                bool on = pcs != null && (i == 0 ? pcs.meleeMode : i == 1 ? !pcs.meleeMode : pcs.scopeMode);
                if (_ring[i].gameObject.activeSelf != on) _ring[i].gameObject.SetActive(on);
            }
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

            // Selector (1 melee, 2 disparo, 3 mira telescopica): placas apiladas; el aro verde marca la activa. Clic = elegir (solo con un menu abierto).
            for (int i = 0; i < 3; i++)
            {
                int id = i;
                var r = Rnd(Img(_uiRoot.transform, new Color(0.02f, 0.03f, 0.03f)));
                _plate[i] = r.GetComponent<Image>();
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(1f, 0f);
                r.sizeDelta = new Vector2(80f, 80f);
                r.anchoredPosition = new Vector2(-30f, 122f + (2 - i) * 90f);
                var btn = _plate[i].gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => { var p = GetPlayer(); if (p == null) return; if (id == 2) p.scopeMode = !p.scopeMode; else p.meleeMode = id == 0; });
                var ring = Img(r, Green);
                _ring[i] = ring.GetComponent<Image>();
                _ring[i].sprite = RingSprite();
                Mid(ring, 70f);
                ring.gameObject.SetActive(false);
                var ic = Img(r, new Color(0.8f, 0.9f, 0.8f));
                ic.GetComponent<Image>().sprite = i == 0 ? FistSprite() : i == 1 ? GunSprite() : ScopeSprite();
                Mid(ic, 48f);
                var n = Txt(r, 22, Amber, TextAlignmentOptions.Center, FontStyles.Bold);
                n.text = (i + 1).ToString();
                n.raycastTarget = false;
                n.rectTransform.anchorMin = n.rectTransform.anchorMax = n.rectTransform.pivot = new Vector2(0f, 1f);
                n.rectTransform.sizeDelta = new Vector2(24f, 24f);
                n.rectTransform.anchoredPosition = new Vector2(4f, -2f);
            }

            // 2 botones metalicos redondos con punto rubi (TAB, J), en la misma fila que la placa de la mira, dentro del margen derecho
            for (int i = 0; i < 2; i++)
            {
                int id = i;
                var pos = new Vector2(-126f - (1 - i) * 76f, 130f);
                if (i == 1) // halo grande detras del boton J
                {
                    var hl = Img(_uiRoot.transform, new Color(1f, 0.05f, 0.03f, 0f));
                    _glow = hl.GetComponent<Image>();
                    _glow.sprite = GlowSprite();
                    _glow.raycastTarget = false;
                    hl.anchorMin = hl.anchorMax = new Vector2(1f, 0f);
                    hl.pivot = new Vector2(0.5f, 0.5f);
                    hl.sizeDelta = new Vector2(300f, 300f);
                    hl.anchoredPosition = pos + new Vector2(-32f, 32f);
                    hl.gameObject.SetActive(false);
                }
                var b = Rnd(Img(_uiRoot.transform, new Color(0.62f, 0.64f, 0.66f)));
                b.GetComponent<Image>().raycastTarget = true;
                b.anchorMin = b.anchorMax = b.pivot = new Vector2(1f, 0f);
                b.sizeDelta = new Vector2(64f, 64f);
                b.anchoredPosition = pos;
                var bt = b.gameObject.AddComponent<Button>();
                bt.transition = Selectable.Transition.None;
                bt.onClick.AddListener(() => OpenPanel(id));
                Mid(Rnd(Img(b, new Color(0.3f, 0.32f, 0.34f))), 50f);
                var dot = Rnd(Img(b, Ruby));
                Mid(dot, 16f);
                if (i == 1) _jDot = dot.GetComponent<Image>();
                var l = Txt(b, 16, Amber, TextAlignmentOptions.Center, FontStyles.Bold);
                l.text = PanelLbl[i];
                l.raycastTarget = false;
                Fit(l.rectTransform, 0f, 1f, 1f, 1f);
                l.rectTransform.offsetMin = new Vector2(0f, 4f);
                l.rectTransform.offsetMax = new Vector2(0f, 24f);
            }
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

        // ---- Datos para el panel de personaje (CharacterPanel) ----
        public float BarFraction(int i) => _lastF[i];
        public string BarText(int i) => _barTxt[i] != null ? _barTxt[i].text : null;
        public string TimeStr => _cachedTimeStr;

        /// <summary>Skills base del jugador: disparo, recarga, cuerpo a cuerpo, escudo, gancho, sprint, jetpack y mira telescopica.</summary>
        public void CollectSkills(System.Collections.Generic.List<string> names, System.Collections.Generic.List<string> keys)
        {
            var pc = GetPlayer();
            var shooter = GetShooter();
            var shield = GetShield();
            var prop = GetPropulsion();

            if (shooter != null && (shooter.isUnlocked || shooter.Projectile != null))
            {
                names.Add(shooter.HoldToFire ? "Disparo continuo" : "Disparo"); keys.Add("2+LMB");
                names.Add("Recarga"); keys.Add("R");
            }
            if (pc != null && pc.HasMelee) { names.Add("Cuerpo a cuerpo"); keys.Add("1+LMB"); }
            if (shield != null && shield.isUnlocked) { names.Add("Escudo"); keys.Add("MMB"); }
            if (pc != null && pc.HasHook) { names.Add("Gancho"); keys.Add("RMB+G"); }
            names.Add("Sprint"); keys.Add("Shift");
            if (prop != null && prop.MaxJetpack > 0) { names.Add("Jetpack"); keys.Add("Space/B"); }
            names.Add(pc != null && pc.scopeMode ? "Mira telescopica (ON)" : "Mira telescopica"); keys.Add("3");
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

        static Sprite FistSprite() => s_fist ??= Draw((x, y) =>
        {
            float a = Mathf.Max(Box(x, y, 20f, 12f, 46f, 38f), Mathf.Max(Box(x, y, 26f, 4f, 42f, 12f), Disc(x, y, 17f, 27f, 6f)));
            for (int k = 0; k < 4; k++) a = Mathf.Max(a, Disc(x, y, 24f + k * 6.7f, 38f, 5.3f));
            return a;
        });

        static Sprite GunSprite() => s_gun ??= Draw((x, y) =>
            Mathf.Max(Box(x, y, 12f, 36f, 52f, 46f), Mathf.Max(Box(x, y, 14f, 14f, 28f, 36f), Box(x, y, 28f, 28f, 31f, 36f))));

        static float Box(float x, float y, float x0, float y0, float x1, float y1) => Band(x, x0, x1) * Band(y, y0, y1);
        static float Disc(float x, float y, float cx, float cy, float r) => Mathf.Clamp01(r - Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) + 0.5f);

        static Sprite RingSprite()
        {
            if (s_ring != null) return s_ring;
            return s_ring = Draw((x, y) =>
            {
                float dx = x - 31.5f, dy = y - 31.5f;
                return Band(Mathf.Sqrt(dx * dx + dy * dy), 27f, 31.5f);
            });
        }

        // Resplandor radial suave (alfa cuadratica hacia el borde).
        static Sprite GlowSprite()
        {
            if (s_glow != null) return s_glow;
            return s_glow = Draw((x, y) =>
            {
                float dx = x - 31.5f, dy = y - 31.5f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / 32f);
                return a * a;
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