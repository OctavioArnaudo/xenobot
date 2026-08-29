using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

/// <summary>
/// Sistema de salud y jetpack del jugador.
/// HUD implementado en OnGUI para coincidir con StatsHUD.
/// </summary>
public class PlayerHealth : NetworkBehaviour
{
    [Header("Configuración")]
    public int maxHealth = 100;
    public float maxJetpack = 100f;

    [Header("HUD Style")]
    public int barWidth = 180;
    public int barHeight = 8;
    public int fontSize = 14;
    public int margin = 0; // Pegado a la esquina

    private NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private int m_OfflineHealth;
    private float m_Jetpack;

    public float JetpackFuel => m_Jetpack;
    public float MaxJetpackFuel => maxJetpack;
    public float CurrentHealth => IsNetworkActive ? currentHealth.Value : m_OfflineHealth;

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    // Assets para OnGUI
    private Texture2D _bg, _barBg, _hpFill, _jetFill;
    private GUIStyle _labelStyle, _valueStyle;
    private bool _stylesReady;

    void Awake()
    {
        m_OfflineHealth = maxHealth;
    }

    void Start()
    {
        m_Jetpack = maxJetpack;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) currentHealth.Value = maxHealth;
        m_Jetpack = maxJetpack;
    }

    public void UseFuel(float amount) => m_Jetpack = Mathf.Max(0f, m_Jetpack - amount);
    public void AddFuel(float amount) => m_Jetpack = Mathf.Min(maxJetpack, m_Jetpack + amount);

    [Rpc(SendTo.Server)]
    public void ApplyDamageRpc(int damage) => ApplyNetworkDamage(damage);

    public void TakeDamage(int damage)
    {
        if (IsNetworkActive)
        {
            if (IsServer) ApplyNetworkDamage(damage);
            else ApplyDamageRpc(damage);
        }
        else
        {
            m_OfflineHealth = Mathf.Max(0, m_OfflineHealth - damage);
        }
    }

    void ApplyNetworkDamage(int damage)
    {
        if (!IsServer || currentHealth.Value <= 0) return;
        currentHealth.Value -= damage;
    }

    void EnsureStyles()
    {
        if (_stylesReady) return;

        _bg = MakeTex(new Color(0f, 0f, 0f, 0.7f));
        _barBg = MakeTex(new Color(0.15f, 0.15f, 0.15f, 0.9f));
        _hpFill = MakeTex(new Color(0.85f, 0.1f, 0.1f, 1f));   // Rojo Vida
        _jetFill = MakeTex(new Color(0.1f, 0.85f, 0.85f, 1f)); // Cian Jetpack

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        _labelStyle.normal.textColor = Color.white;

        _valueStyle = new GUIStyle(_labelStyle)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Normal
        };
        _valueStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

        _stylesReady = true;
    }

    Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void OnGUI()
    {
        if (!IsOwner) return;
        if (SceneManager.GetActiveScene().name != "BiomaScene") return;

        EnsureStyles();

        // Calcular dimensiones (2 filas: Jetpack arriba, Vida abajo)
        int rowH = fontSize + barHeight + 2;
        int totalHeight = rowH * 2 + 4;
        int totalWidth = barWidth;

        // Posición: esquina inferior izquierda extrema
        float x = 0;
        float y = Screen.height - totalHeight;

        // Fondo
        GUI.DrawTexture(new Rect(x, y, totalWidth, totalHeight), _bg);

        float curY = y + 2;

        // --- JETPACK (Arriba) ---
        DrawStatRow(x, ref curY, totalWidth, " JET", m_Jetpack, maxJetpack, _jetFill);

        // --- VIDA (Abajo) ---
        DrawStatRow(x, ref curY, totalWidth, " HP", CurrentHealth, maxHealth, _hpFill);
    }

    void DrawStatRow(float x, ref float y, float w, string label, float val, float max, Texture2D fill)
    {
        // Texto
        GUI.Label(new Rect(x, y, w - 2, fontSize + 2), label, _labelStyle);
        GUI.Label(new Rect(x, y, w - 2, fontSize + 2), val.ToString("F0"), _valueStyle);
        y += fontSize + 2;

        // Barra
        Rect bgRect = new Rect(x, y, w, barHeight);
        GUI.DrawTexture(bgRect, _barBg);

        float ratio = Mathf.Clamp01(val / max);
        GUI.DrawTexture(new Rect(x, y, w * ratio, barHeight), fill);

        y += barHeight;
    }
}
