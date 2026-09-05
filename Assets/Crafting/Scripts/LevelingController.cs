using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Specialized controller for character leveling and progression logic.
/// </summary>
public class LevelingController : NetworkBehaviour
{
    private HudController _stats;

    void Awake()
    {
        _stats = GetComponent<HudController>();
    }

    public override void OnNetworkSpawn()
    {
        if (_stats == null) _stats = GetComponent<HudController>();
    }

    public void AddExp(float amount)
    {
        if (_stats != null)
        {
            _stats.AddExp(amount);
        }
    }
}
