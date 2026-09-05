using UnityEngine;
using Unity.Netcode;
using UnityEngine.Events;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for character Death events and destruction logic.
    /// </summary>
    public class DeathController : NetworkBehaviour, IModular
    {
        [Header("Events")]
        public UnityEvent OnDeath;

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

        public void Die()
        {
            OnDeath?.Invoke();

            var spawnCtrl = (_hub != null) ? _hub.GetModule<SpawnController>() : GetComponent<SpawnController>();
            if (spawnCtrl != null)
            {
                spawnCtrl.TriggerDeath();
            }
            else
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned && IsServer)
                {
                    GetComponent<NetworkObject>().Despawn(false);
                    Destroy(gameObject);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
