using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using NGO.Networking;
using Crafting.Scripts;

/// <summary>
/// Specialized controller for character progression and stats.
/// Acts as the data source for the player's attributes.
/// </summary>
public class HudController : NetworkBehaviour
{
    public static HudController Instance { get; private set; }

    [Header("Identity & Visuals")]
    public NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(new FixedString32Bytes(""), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<Color> playerColor = new NetworkVariable<Color>(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [Header("Initial Ranges")]
    public Vector2 attackRange = new Vector2(5f, 15f);
    public Vector2 defenseRange = new Vector2(3f, 10f);

    [Header("Base Growth")]
    public float attackPerLevel = 2f;
    public float defensePerLevel = 1.5f;
    public float expToLevelUp = 100f;

    public float Attack { get; private set; }
    public float Defense { get; private set; }
    public int Level { get; private set; } = 1;
    public float Exp { get; private set; }

    private Combating.Scripts.FuelController m_PlayerHealth;

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    void Awake()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            InitializeStats();
            ResolveHealthReference();
        }
    }

    public override void OnNetworkSpawn()
    {
        ResolveHealthReference();
        if (IsOwner)
        {
            Instance = this;
            InitializeStats();
            if (LocalUserConfig.UserName != null) playerName.Value = LocalUserConfig.UserName;
            playerColor.Value = LocalUserConfig.UserColor;
        }
    }

    private void ResolveHealthReference()
    {
        var hub = GetComponentInParent<PlayerController>();
        if (hub != null)
        {
            m_PlayerHealth = hub.GetComponentInChildren<Combating.Scripts.FuelController>();
        }

        if (m_PlayerHealth == null)
            m_PlayerHealth = GetComponent<Combating.Scripts.FuelController>();
    }

    void InitializeStats()
    {
        Attack = Random.Range(attackRange.x, attackRange.y);
        Defense = Random.Range(defenseRange.x, defenseRange.y);
    }

    public void AddExp(float amount)
    {
        Exp += amount;
        while (Exp >= expToLevelUp) { Exp -= expToLevelUp; LevelUp(); }
    }

    void LevelUp()
    {
        Level++;
        Attack += attackPerLevel;
        Defense += defensePerLevel;
        expToLevelUp *= 1.2f;

        if (m_PlayerHealth != null)
        {
            m_PlayerHealth.UpgradeMaxStats(15, 20f);
        }
    }
}
