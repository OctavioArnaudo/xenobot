using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

/// <summary>
/// Specialized controller for character leveling and progression logic.
/// </summary>
public class LevelingController : NetworkBehaviour, IPlayer
{
    private HudController _stats;
    private PlayerController _hub;

    void Awake()
    {
        _hub = GetComponentInParent<PlayerController>();
        if (_hub != null) Bind(_hub);
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
        }
    }

    public void AddExp(float amount)
    {
        if (_stats != null)
        {
            _stats.AddExp(amount);
        }
    }
}
