using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Combating.Scripts;
using Unity.Collections;
using NGO.Networking;
using TMPro;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Combating.Scripts
{
    /// <summary>
    /// Unified controller for character progression, HUD and Identity.
    /// Optimized to reduce CPU overhead and audio starvation.
    /// </summary>
    public class HudController : NetworkBehaviour
    {
        public static HudController Instance { get; private set; }

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
            var keys = new System.Collections.Generic.List<string>();
            var pc = GetPlayer();
            var prop = GetPropulsion();

            bool isMoving = pc != null && pc.move.sqrMagnitude > 0.01f;
            bool isSprinting = pc != null && pc.sprint;
            bool isGrounded = pc == null || pc.Grounded;
            bool isFlying = prop != null && prop.IsFlying;
            bool hasJet = prop != null && prop.MaxJetpack > 0;

            if (!isMoving) keys.Add("WASD: Caminar");
            if (isMoving && !isSprinting) keys.Add("Shift: Correr");
            if (isGrounded) keys.Add("Space x2: Salto Dbl");
            if (hasJet && !isFlying) keys.Add("Space+B: Volar");

            return keys;
        }

        private System.Collections.Generic.List<string> GetDynamicAttackKeys()
        {
            var keys = new System.Collections.Generic.List<string>();
            var pc = GetPlayer();
            var shooter = GetShooter();
            var shield = GetShield();

            bool isFiring = pc != null && (pc.fire || pc.fireHeld);
            bool isShieldActive = shield != null && shield.IsShieldActive;
            bool isReloading = shooter != null && shooter.isReloading;
            bool needsAmmo = shooter != null && shooter.currentAmmo < shooter.EffectiveMaxAmmo;

            if (!isFiring) keys.Add("LMB: Atacar");
            if (shield != null && !isShieldActive) keys.Add("RMB: Escudo");
            if (shooter != null && !isReloading && needsAmmo) keys.Add("R: Recargar");

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

        void Awake()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                if (Instance != null && Instance != this) { Destroy(this); return; }
                Instance = this;
                InitializeStats();

                // Inicialización offline para HUD y Visuales
                m_PlayerHealth = GetComponent<HealthController>();
                UpdateVisuals();
            }
        }

        public override void OnNetworkSpawn()
        {
            m_PlayerHealth = GetComponent<HealthController>();

            // SERVER: Initialize stats for EVERY player spawned
            if (IsServer) InitializeStats();

            if (IsOwner)
            {
                Instance = this;
                playerName.Value = LocalUserConfig.UserName;
                playerColor.Value = LocalUserConfig.UserColor;
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
                dName = LocalUserConfig.UserName;
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
                    if (stats.attackPerLevel > 0) return stats.attackPerLevel;
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
                    if (stats.defensePerLevel > 0) return stats.defensePerLevel;
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
        // Se construye una sola vez. Después solo se actualizan los valores (10 veces por segundo)
        // y el panel de stats se activa o desactiva con K.
        static readonly Color Ochre = new Color(0.8f, 0.47f, 0.13f);
        static readonly Color[] BarCol =
        {
            new Color(0.9f, 0.1f, 0.1f), new Color(0f, 0.9f, 0.9f), new Color(1f, 0.8f, 0.1f), new Color(0.7f, 0.3f, 1f)
        };

        GameObject _uiRoot, _statsPanel;
        readonly RectTransform[] _fill = new RectTransform[4];
        readonly TextMeshProUGUI[] _barTxt = new TextMeshProUGUI[4];
        readonly float[] _lastF = new float[4];
        TextMeshProUGUI _keysL, _keysR, _statsTxt;
        float _uiTick;

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (_uiRoot != null) Destroy(_uiRoot);
        }

        void UpdateHud()
        {
            if (!CanExecuteLocalLogic) return;

            // Tiempo de sesión (lo usan las pantallas de victoria y derrota): cada 0,5 s
            if (Time.time - _lastTimeUpdate > 0.5f)
            {
                _lastTimeUpdate = Time.time;
                _inBioma = SceneManager.GetActiveScene().name == "BiomaScene";
                if (_inBioma)
                {
                    float t = Time.timeSinceLevelLoad;
                    LevelsMenu.ultimoTiempoSession = t;
                    LevelsMenu.ultimoNivelSession = SceneManager.GetActiveScene().name;
                    _cachedTimeStr = LevelsMenu.FormatTime(t);
                }
            }
            if (!_inBioma || m_PlayerHealth == null) return;
            if (_uiRoot == null) BuildHud();

            if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
            {
                _statsPanel.SetActive(!_statsPanel.activeSelf);
                RefreshStats();
            }

            if (Time.unscaledTime < _uiTick) return;
            _uiTick = Time.unscaledTime + 0.1f;
            RefreshBars();
            if (_statsPanel.activeSelf) RefreshStats();
        }

        void BuildHud()
        {
            _uiRoot = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler));
            var c = _uiRoot.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 90;
            var cs = _uiRoot.GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080);

            // 4 barras a lo largo del borde inferior: HP, JET, AMMO, SHIELD
            for (int i = 0; i < 4; i++)
            {
                var cell = Img(_uiRoot.transform, new Color(0.08f, 0.08f, 0.08f, 0.85f));
                cell.anchorMin = new Vector2(i * 0.25f, 0f);
                cell.anchorMax = new Vector2((i + 1) * 0.25f, 0f);
                cell.pivot = new Vector2(0.5f, 0f);
                cell.sizeDelta = new Vector2(-6f, 64f);
                cell.anchoredPosition = Vector2.zero;
                _fill[i] = Img(cell, BarCol[i]);
                Fit(_fill[i], 0f, 0f, 1f, 1f);
                var t = Txt(cell, 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                Fit(t.rectTransform, 0f, 0f, 1f, 1f);
                _barTxt[i] = t;
            }

            // Teclas disponibles, encima de las barras (izquierda: movimiento, derecha: combate)
            var keyCol = new Color(0.95f, 0.95f, 0.5f);
            _keysL = Txt(_uiRoot.transform, 22, keyCol, TextAlignmentOptions.BottomLeft, FontStyles.Bold);
            var r = _keysL.rectTransform;
            r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0.5f, 0f); r.pivot = new Vector2(0f, 0f);
            r.sizeDelta = new Vector2(-16f, 30f); r.anchoredPosition = new Vector2(8f, 68f);
            _keysR = Txt(_uiRoot.transform, 22, keyCol, TextAlignmentOptions.BottomRight, FontStyles.Bold);
            r = _keysR.rectTransform;
            r.anchorMin = new Vector2(0.5f, 0f); r.anchorMax = new Vector2(1f, 0f); r.pivot = new Vector2(1f, 0f);
            r.sizeDelta = new Vector2(-16f, 30f); r.anchoredPosition = new Vector2(-8f, 68f);

            // Panel de stats: grande, centrado, ocre (K)
            var pr = Img(_uiRoot.transform, new Color(0f, 0f, 0f, 0.92f));
            var ol = pr.gameObject.AddComponent<Outline>();
            ol.effectColor = Ochre;
            ol.effectDistance = new Vector2(5f, 5f);
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
            pr.anchoredPosition = Vector2.zero;
            pr.sizeDelta = new Vector2(900f, 500f);
            var title = Txt(pr, 56, Ochre, TextAlignmentOptions.Center, FontStyles.Bold);
            title.text = "STATS";
            Fit(title.rectTransform, 0f, 0.8f, 1f, 1f);
            _statsTxt = Txt(pr, 40, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Normal);
            Fit(_statsTxt.rectTransform, 0.1f, 0.05f, 0.9f, 0.8f);
            _statsPanel = pr.gameObject;
            _statsPanel.SetActive(false);
        }

        void RefreshBars()
        {
            var prop = GetPropulsion();
            var shooter = GetShooter();
            var shield = GetShield();

            Bar(0, "HP", m_PlayerHealth.CurrentHP, m_PlayerHealth.EffectiveMaxHealth,
                $"{m_PlayerHealth.CurrentHP}/{m_PlayerHealth.EffectiveMaxHealth}");

            if (prop != null && prop.MaxJetpack > 0) Bar(1, "JET", prop.JetpackFuel, prop.MaxJetpack, $"{prop.JetpackFuel:F0}/{prop.MaxJetpack:F0}");
            else Bar(1, "JET", 0f, 1f, "N/A");

            if (shooter != null)
                Bar(2, "AMMO", shooter.currentAmmo, shooter.EffectiveMaxAmmo,
                    shooter.isReloading ? "RECARGANDO" : $"{shooter.currentAmmo}/{shooter.EffectiveMaxAmmo}");
            else Bar(2, "AMMO", 0f, 1f, "SIN ARMA");

            if (shield != null)
            {
                float val = shield.CurrentShieldHealth;
                float max = shield.EffectiveMaxShieldHealth > 0 ? shield.EffectiveMaxShieldHealth : 100f;
                string str = $"{val:F0}/{max:F0}";
                if (shield.InCooldown)
                {
                    val = shield.CooldownProgress * max;
                    str = $"RECARGA {shield.CooldownRemaining:F1}s";
                }
                else if (!shield.isUnlocked)
                {
                    val = 0f;
                    str = "BLOQUEADO";
                }
                Bar(3, "SHIELD", val, max, str);
            }
            else Bar(3, "SHIELD", 0f, 1f, "N/A");

            _keysL.text = string.Join("   ", GetDynamicDefenseKeys());
            _keysR.text = string.Join("   ", GetDynamicAttackKeys());
        }

        void Bar(int i, string name, float val, float max, string str)
        {
            float f = Mathf.Clamp01(val / (max > 0f ? max : 1f));
            if (!Mathf.Approximately(f, _lastF[i]))
            {
                _lastF[i] = f;
                _fill[i].anchorMax = new Vector2(f, 1f);
            }
            _barTxt[i].text = name + "  " + str;
        }

        void RefreshStats()
        {
            _statsTxt.text = $"TIME<pos=45%>{_cachedTimeStr}\nATK<pos=45%>{Attack:F0}\nDEF<pos=45%>{Defense:F0}\nLVL<pos=45%>{Level}\nEXP<pos=45%>{Exp:F0} / {ExpToLevelUp:F0}";
        }

        static RectTransform Img(Transform parent, Color col)
        {
            var g = new GameObject("i", typeof(Image));
            g.transform.SetParent(parent, false);
            var im = g.GetComponent<Image>();
            im.color = col;
            im.raycastTarget = false;
            return g.GetComponent<RectTransform>();
        }

        static TextMeshProUGUI Txt(Transform parent, int size, Color col, TextAlignmentOptions align, FontStyles style)
        {
            var g = new GameObject("t", typeof(TextMeshProUGUI));
            g.transform.SetParent(parent, false);
            var t = g.GetComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = col;
            t.alignment = align;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        static void Fit(RectTransform r, float x0, float y0, float x1, float y1)
        {
            r.anchorMin = new Vector2(x0, y0);
            r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }
    }
}