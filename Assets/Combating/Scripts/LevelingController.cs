using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;
using Combating.Scripts;

public class LevelingController : NetworkBehaviour, IModular
{
    private HudController _stats;
    private ModularController _hub;

    void Awake()
    {
        _hub = GetComponentInParent<ModularController>();
        if (_hub != null) Bind(_hub);
    }

    public void Bind(ModularController hub)
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
