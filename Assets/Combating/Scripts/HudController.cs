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
public class StatsController : NetworkBehaviour
{
    public static StatsController Instance { get; private set; }

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

    private Texture2D _bg, _barBg, _atkFill, _defFill, _expFill, _hpFill, _jetFill;
    private GUIStyle _labelStyle, _valueStyle, _timerStyle;
    private bool _stylesReady;

    private HealthController m_PlayerHealth;
    private float _lastTimeUpdate;
    private string _cachedTimeStr = "00:00";
    private Camera _mainCamCache;

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
        if (IsOwner)
        {
            Instance = this;
            InitializeStats();
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

        if (IsNetworkActive && IsServer)
        {
            NetAttack.Value = atk;
            NetDefense.Value = def;
            NetLevel.Value = 1;
            NetExp.Value = 0;
            NetExpToLevelUp.Value = 100f;
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

    [Rpc(SendTo.Server)]
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
            // Expandir maximos y curar un poco (ej: 15 HP y 20 Fuel extra por nivel)
            m_PlayerHealth.UpgradeMaxStats(15, 20f);
        }
    }

    void EnsureAssets()
    {
        if (_stylesReady) return;
        _bg = MakeTex(new Color(0f, 0f, 0f, 0.6f));
        _barBg = MakeTex(new Color(0.1f, 0.1f, 0.1f, 0.8f));
        _atkFill = MakeTex(new Color(1f, 0.4f, 0f, 1f));
        _defFill = MakeTex(new Color(0f, 0.6f, 1f, 1f));
        _expFill = MakeTex(new Color(1f, 0.9f, 0f, 1f));
        _hpFill = MakeTex(new Color(0.9f, 0.1f, 0.1f, 1f));
        _jetFill = MakeTex(new Color(0f, 0.9f, 0.9f, 1f));

        _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        _valueStyle = new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Normal };
        _timerStyle = new GUIStyle(_labelStyle) { alignment = TextAnchor.MiddleCenter, fontSize = fontSize + 2 };
        _timerStyle.normal.textColor = Color.cyan;
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
        if (m_PlayerHealth != null) DrawBottomLeftHUD();
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
        DrawRow(x, ref curY, " ATK", Attack, 100f, _atkFill);
        DrawRow(x, ref curY, " DEF", Defense, 100f, _defFill);
        DrawRow(x, ref curY, $" LVL {Level}", Exp, expToLevelUp, _expFill);
    }

    private void DrawBottomLeftHUD()
    {
        bool hasJet = m_PlayerHealth.maxJetpack > 0;
        int rowH = fontSize + barHeight + 2;
        int totalH = rowH * (hasJet ? 2 : 1) + 4;
        float y = Screen.height - totalH;
        GUI.DrawTexture(new Rect(0, y, barWidth, totalH), _bg);
        float curY = y + 2;
        if (hasJet) DrawRow(0, ref curY, " JET", m_PlayerHealth.JetpackFuel, m_PlayerHealth.maxJetpack, _jetFill);
        DrawRow(0, ref curY, " HP", m_PlayerHealth.CurrentHP, m_PlayerHealth.maxHealth, _hpFill);
    }

    void DrawRow(float x, ref float y, string label, float val, float max, Texture2D fill)
    {
        GUI.Label(new Rect(x, y, barWidth - 2, fontSize + 2), label, _labelStyle);
        GUI.Label(new Rect(x, y, barWidth - 2, fontSize + 2), val.ToString("F0"), _valueStyle);
        y += fontSize + 2;
        GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), _barBg);
        GUI.DrawTexture(new Rect(x, y, barWidth * Mathf.Clamp01(val / (max > 0 ? max : 1f)), barHeight), fill);
        y += barHeight;
    }
}
