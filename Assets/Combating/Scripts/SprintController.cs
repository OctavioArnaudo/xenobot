using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class SprintController : NetworkBehaviour, IModular
    {
        private ModularController _hub;

        private void Awake()
        {
            _hub = GetComponentInParent<ModularController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null) _hub.RegisterModule(this);
        }

        public void OnRefreshModule() { }
    }
}
