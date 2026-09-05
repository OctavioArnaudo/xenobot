using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Crafting.Scripts;
using Combating.Scripts;

/// <summary>
/// Specialized controller for character HUD and Identity visuals.
/// Only handles rendering and visual feedback.
/// </summary>
public class UiController : NetworkBehaviour, IPlayerModule
{
    public static UiController Instance { get; private set; }

    [Header("HUD Config")]
    public int fontSize = 14;
    public int barWidth = 160;
    public int barHeight = 6;
    public TMPro.TMP_Text nameTagText;

    private Texture2D _bg, _barBg, _atkFill, _defFill, _expFill, _hpFill, _jetFill;
    private GUIStyle _labelStyle, _valueStyle, _timerStyle;
    private bool _stylesReady;

    private HudController _stats;
    private Combating.Scripts.FuelController _fuel;
    private HealthController _health;
    private PlayerController _hub;

    private float _lastTimeUpdate;
    private string _cachedTimeStr = "00:00";
    private Camera _mainCamCache;

    private Transform _aureoleRoot;
    private Vector3 _aureoleBaseOffset = new Vector3(0, 2.4f, 0);

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        _hub = GetComponentInParent<PlayerController>();
        if (_hub != null) Bind(_hub);
        else ResolveReferences();
    }

    public void Bind(PlayerController hub)
    {
        _hub = hub;
        if (_hub != null)
        {
            _hub.RegisterModule(this);
            OnRefreshModule();
        }
    }

    public void OnRefreshModule()
    {
        if (_hub != null)
        {
            _stats = _hub.GetModule<HudController>();
            _fuel = _hub.GetModule<Combating.Scripts.FuelController>();
            _health = _hub.GetModule<HealthController>();
            UpdateVisuals();
        }
    }

    public override void OnNetworkSpawn()
    {
        ResolveReferences();

        if (_stats != null)
        {
            _stats.playerName.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
            _stats.playerColor.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
        }
        UpdateVisuals();
    }

    private void ResolveReferences()
    {
        var hub = GetComponentInParent<PlayerController>();
        if (hub != null)
        {
            if (_stats == null) _stats = hub.GetComponentInChildren<HudController>();
            if (_fuel == null) _fuel = hub.GetComponentInChildren<Combating.Scripts.FuelController>();
            if (_health == null) _health = hub.GetComponentInChildren<HealthController>();
        }

        if (_stats == null) _stats = GetComponent<HudController>();
        if (_fuel == null) _fuel = GetComponent<Combating.Scripts.FuelController>();
        if (_health == null) _health = GetComponent<HealthController>();
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
        if (nameTagText != null && _stats != null) nameTagText.text = _stats.playerName.Value.ToString();
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
                Animator anim = GetComponentInChildren<Animator>();
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

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        if (_stats == null || _health == null) ResolveReferences();
        if (!CanExecuteLocalLogic || _stats == null) return;

        EnsureAssets();

        if (Time.time - _lastTimeUpdate > 0.5f)
        {
            float t = Time.timeSinceLevelLoad;
            LevelsMenu.ultimoTiempoSession = t;
            LevelsMenu.ultimoNivelSession = SceneManager.GetActiveScene().name;
            _cachedTimeStr = LevelsMenu.FormatTime(t);
            _lastTimeUpdate = Time.time;
        }

        DrawTopRightHUD();
        DrawBottomLeftHUD();
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
        DrawRow(x, ref curY, " ATK", _stats.Attack, 100f, _atkFill);
        DrawRow(x, ref curY, " DEF", _stats.Defense, 100f, _defFill);
        DrawRow(x, ref curY, $" LVL {_stats.Level}", _stats.Exp, _stats.expToLevelUp, _expFill);
    }

    private void DrawBottomLeftHUD()
    {
        int rowH = fontSize + barHeight + 2;
        int rows = 0;
        if (_fuel != null) rows++;
        if (_health != null) rows++;

        if (rows == 0) return;

        int totalH = rowH * rows + 4;
        float y = Screen.height - totalH;
        GUI.DrawTexture(new Rect(0, y, barWidth, totalH), _bg);
        float curY = y + 2;

        if (_fuel != null)
            DrawRow(0, ref curY, " JET", _fuel.JetpackFuel, _fuel.maxJetpack, _jetFill);

        if (_health != null)
            DrawRow(0, ref curY, " HP", _health.CurrentHP, _health.maxHealth, _hpFill);
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
}
