using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine.SceneManagement;
using NGO.Networking;
using Crafting.Scripts;
using Combating.Scripts;

/// <summary>
/// Specialized controller for character progression and stats.
/// Acts as the data source for the player's attributes.
/// </summary>
public class HudController : MonoBehaviour, IModular
{
    public static HudController Instance { get; private set; }

    [Header("GUI Config")]
    public int fontSize = 14;
    public int barWidth = 160;
    public int barHeight = 6;
    public System.Collections.Generic.List<string> allowedScenes = new System.Collections.Generic.List<string> { "BiomaScene" };

    private HealthController m_Health;
    private TankController m_Tank;
    private ModularController _hub;

    private Texture2D _bg, _barBg, _atkFill, _defFill, _expFill, _hpFill, _jetFill;
    private GUIStyle _labelStyle, _valueStyle, _timerStyle;
    private bool _stylesReady;
    private float _lastTimeUpdate;
    private string _cachedTimeStr = "00:00";

    void Awake()
    {
        // Hub will call Bind() manually during module assembly.
        if (_hub == null) _hub = GetComponentInParent<ModularController>();
    }

    public void Bind(ModularController hub)
    {
        _hub = hub;
        if (_hub != null)
        {
            if (_hub.IsOwner || (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            {
                Instance = this;
            }

            _hub.RegisterModule(this);
            OnRefreshModule();
        }
    }

    public void OnRefreshModule()
    {
        if (_hub != null)
        {
            m_Health = _hub.GetModule<HealthController>();
            m_Tank = _hub.GetModule<TankController>();
        }
    }

    private bool CanExecuteLocalLogic => _hub != null && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || _hub.IsOwner);

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        if (!CanExecuteLocalLogic) return;

        // Dynamic Resolution: Try to find missing modules if they aren't linked yet
        if (_hub != null && (m_Health == null || m_Tank == null))
        {
            OnRefreshModule();
        }

        string currentScene = SceneManager.GetActiveScene().name;

        // Safety check: Default to BiomaScene if list is not properly initialized
        bool isAllowed = false;
        if (allowedScenes == null || allowedScenes.Count == 0)
        {
            if (currentScene == "BiomaScene") isAllowed = true;
        }
        else
        {
            if (allowedScenes.Contains(currentScene)) isAllowed = true;
        }

        if (!isAllowed) return;

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
        if (_hub == null) return;

        int rowH = fontSize + barHeight + 2;
        int totalH = rowH * 4 + 6;
        float x = Screen.width - barWidth;
        GUI.DrawTexture(new Rect(x, 0, barWidth, totalH), _bg);
        float curY = 2;
        GUI.Label(new Rect(x, curY, barWidth, fontSize + 4), $"TIME {_cachedTimeStr}", _timerStyle);
        curY += fontSize + 6;
        DrawRow(x, ref curY, " ATK", _hub.Attack.Value, 100f, _atkFill);
        DrawRow(x, ref curY, " DEF", _hub.Defense.Value, 100f, _defFill);
        DrawRow(x, ref curY, $" LVL {_hub.Level.Value}", _hub.Exp.Value, _hub.ExpToLevelUp.Value, _expFill);
    }

    private void DrawBottomLeftHUD()
    {
        int rowH = fontSize + barHeight + 2;
        int rows = 0;
        if (m_Tank != null) rows++;
        if (m_Health != null) rows++;

        if (rows == 0) return;

        int totalH = rowH * rows + 4;
        float y = Screen.height - totalH;
        GUI.DrawTexture(new Rect(0, y, barWidth, totalH), _bg);
        float curY = y + 2;

        if (m_Tank != null)
            DrawRow(0, ref curY, " JET", _hub.currentFuel.Value, _hub.maxFuel.Value, _jetFill);

        if (m_Health != null)
            DrawRow(0, ref curY, " HP", _hub.currentHealth.Value, _hub.maxHealth.Value, _hpFill);
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
