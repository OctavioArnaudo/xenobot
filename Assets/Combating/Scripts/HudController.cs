using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Combating.Scripts;
using Unity.Collections;
using NGO.Networking;

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

    [Header("HUD Config")]
    public int fontSize = 14;
    public int barWidth = 160;
    public int barHeight = 6;

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

    private Texture2D _bg, _barBg, _atkFill, _defFill, _expFill, _hpFill, _jetFill, _ammoFill, _shieldFill;
    private GUIStyle _labelStyle, _valueStyle, _timerStyle, _keyStyle;
    private bool _stylesReady;

    private HealthController m_PlayerHealth;
    private PropulsionController _propulsion;
    private ShootController _shooter;
    private ShieldController _shield;
    private PlayerController _player;

    private float _lastTimeUpdate;
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

    void LevelUp()
    {
        if (IsNetworkActive)
        {
            NetLevel.Value++;
            NetAttack.Value += attackPerLevel;
            NetDefense.Value += defensePerLevel;
            NetExpToLevelUp.Value *= 1.2f;
        }
        else
        {
            m_OfflineLevel++;
            m_OfflineAttack += attackPerLevel;
            m_OfflineDefense += defensePerLevel;
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

    void EnsureAssets()
    {
        if (_stylesReady) return;
        _bg = MakeTex(new Color(0f, 0f, 0f, 0.65f));
        _barBg = MakeTex(new Color(0.1f, 0.1f, 0.1f, 0.8f));
        _atkFill = MakeTex(new Color(1f, 0.4f, 0f, 1f));
        _defFill = MakeTex(new Color(0f, 0.6f, 1f, 1f));
        _expFill = MakeTex(new Color(1f, 0.9f, 0f, 1f));
        _hpFill = MakeTex(new Color(0.9f, 0.1f, 0.1f, 1f));
        _jetFill = MakeTex(new Color(0f, 0.9f, 0.9f, 1f));
        _ammoFill = MakeTex(new Color(1f, 0.8f, 0.1f, 1f));
        _shieldFill = MakeTex(new Color(0.7f, 0.3f, 1f, 1f));

        _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        _valueStyle = new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Normal };
        _timerStyle = new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleCenter, fontSize = fontSize + 2 };
        _timerStyle.normal.textColor = Color.cyan;

        _keyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(10, fontSize - 3),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
            clipping = TextClipping.Clip
        };
        _keyStyle.normal.textColor = new Color(0.95f, 0.95f, 0.5f, 1f);

        _stylesReady = true;
    }

    Texture2D MakeTex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (SceneManager.GetActiveScene().name != "BiomaScene") return;

        // Use CanExecuteLocalLogic to allow HUD in offline mode or as owner
        if (!CanExecuteLocalLogic) return;

        EnsureAssets();

        // Timer Cache (once per 0.5s)
        if (Time.time - _lastTimeUpdate > 0.5f)
        {
            float t = Time.timeSinceLevelLoad;
            LevelsMenu.ultimoTiempoSession = t;
            LevelsMenu.ultimoNivelSession = SceneManager.GetActiveScene().name;
            _cachedTimeStr = LevelsMenu.FormatTime(t);
            _lastTimeUpdate = Time.time;
        }

        DrawTopRightHUD();
        if (m_PlayerHealth != null)
        {
            DrawBottomLeftHUD();
            DrawBottomRightHUD();
        }
    }

    private void DrawTopRightHUD()
    {
        int rowH = fontSize + barHeight + 2;
        int totalH = rowH * 4 + 6;
        float x = Screen.width - barWidth;
        GUI.DrawTexture(new Rect(x, 0, barWidth, totalH), _bg);
        float curY = 2;
        GUI.Label(new Rect(x, curY, barWidth, fontSize + 4), $"TIME {_cachedTimeStr}", _timerStyle);
        curY += fontSize + 6;
        DrawRow(x, ref curY, " ATK", Attack, 100f, _atkFill, barWidth);
        DrawRow(x, ref curY, " DEF", Defense, 100f, _defFill, barWidth);
        DrawRow(x, ref curY, $" LVL {Level}", Exp, expToLevelUp, _expFill, barWidth);
    }

    private void DrawBottomLeftHUD()
    {
        var prop = GetPropulsion();
        bool hasJet = prop != null && prop.MaxJetpack > 0;
        int rowH = fontSize + barHeight + 2;
        int barsH = rowH * (hasJet ? 2 : 1) + 4;

        var defKeys = GetDynamicDefenseKeys();
        int keysLineCount = defKeys.Count;
        int keysH = keysLineCount * 14;

        float panelWidth = Mathf.Max(barWidth, 190);
        float y = Screen.height - barsH;

        // Draw Keys List ABOVE the bars panel
        if (keysLineCount > 0)
        {
            float keysY = y - keysH - 4;
            GUI.DrawTexture(new Rect(0, keysY, panelWidth, keysH + 2), _bg);
            float curKeyY = keysY + 1;
            foreach (var k in defKeys)
            {
                GUI.Label(new Rect(6, curKeyY, panelWidth - 8, 14), k, _keyStyle);
                curKeyY += 14;
            }
        }

        // Draw Bars Panel
        GUI.DrawTexture(new Rect(0, y, panelWidth, barsH), _bg);
        float curY = y + 2;

        if (m_PlayerHealth != null) DrawRow(0, ref curY, " HP", m_PlayerHealth.CurrentHP, m_PlayerHealth.EffectiveMaxHealth, _hpFill, panelWidth);
        if (hasJet) DrawRow(0, ref curY, " JET", prop.JetpackFuel, prop.MaxJetpack, _jetFill, panelWidth);
    }

    private void DrawBottomRightHUD()
    {
        var shooter = GetShooter();
        var shield = GetShield();

        int rowH = fontSize + barHeight + 2;
        int barsH = rowH * 2 + 4;

        var atkKeys = GetDynamicAttackKeys();
        int keysLineCount = atkKeys.Count;
        int keysH = keysLineCount * 14;

        float panelWidth = Mathf.Max(barWidth, 190);
        float x = Screen.width - panelWidth;
        float y = Screen.height - barsH;

        // Draw Keys List ABOVE the bars panel
        if (keysLineCount > 0)
        {
            float keysY = y - keysH - 4;
            GUI.DrawTexture(new Rect(x, keysY, panelWidth, keysH + 2), _bg);
            float curKeyY = keysY + 1;
            foreach (var k in atkKeys)
            {
                GUI.Label(new Rect(x + 6, curKeyY, panelWidth - 8, 14), k, _keyStyle);
                curKeyY += 14;
            }
        }

        // Draw Bars Panel
        GUI.DrawTexture(new Rect(x, y, panelWidth, barsH), _bg);
        float curY = y + 2;

        // Ammo Bar
        if (shooter != null)
        {
            string ammoValStr = shooter.isReloading ? "RECARGANDO" : $"{shooter.currentAmmo}/{shooter.EffectiveMaxAmmo}";
            DrawRowCustomVal(x, ref curY, " AMMO", shooter.currentAmmo, shooter.EffectiveMaxAmmo, _ammoFill, ammoValStr, panelWidth);
        }
        else
        {
            DrawRowCustomVal(x, ref curY, " AMMO", 0, 100, _ammoFill, "SIN ARMA", panelWidth);
        }

        // Shield Bar
        if (shield != null)
        {
            float shieldVal = shield.IsShieldActive ? 100f : 0f;
            string shieldValStr = shield.IsShieldActive ? "ACTIVO" : "LISTO";
            DrawRowCustomVal(x, ref curY, " SHIELD", shieldVal, 100f, _shieldFill, shieldValStr, panelWidth);
        }
        else
        {
            DrawRowCustomVal(x, ref curY, " SHIELD", 0, 100, _shieldFill, "N/A", panelWidth);
        }
    }

    void DrawRow(float x, ref float y, string label, float val, float max, Texture2D fill, float width)
    {
        GUI.Label(new Rect(x, y, width - 2, fontSize + 2), label, _labelStyle);
        GUI.Label(new Rect(x, y, width - 2, fontSize + 2), val.ToString("F0"), _valueStyle);
        y += fontSize + 2;
        GUI.DrawTexture(new Rect(x, y, width, barHeight), _barBg);
        GUI.DrawTexture(new Rect(x, y, width * Mathf.Clamp01(val / (max > 0 ? max : 1f)), barHeight), fill);
        y += barHeight;
    }

    void DrawRowCustomVal(float x, ref float y, string label, float val, float max, Texture2D fill, string valDisplay, float width)
    {
        GUI.Label(new Rect(x, y, width - 2, fontSize + 2), label, _labelStyle);
        GUI.Label(new Rect(x, y, width - 2, fontSize + 2), valDisplay, _valueStyle);
        y += fontSize + 2;
        GUI.DrawTexture(new Rect(x, y, width, barHeight), _barBg);
        GUI.DrawTexture(new Rect(x, y, width * Mathf.Clamp01(val / (max > 0 ? max : 1f)), barHeight), fill);
        y += barHeight;
    }
}
