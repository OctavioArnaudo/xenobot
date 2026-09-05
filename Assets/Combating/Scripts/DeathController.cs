using UnityEngine;
using Unity.Netcode;
using UnityEngine.Events;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Specialized controller for character Death events and destruction logic.
    /// </summary>
    public class DeathController : NetworkBehaviour, IPlayer
    {
        [Header("Events")]
        public UnityEvent OnDeath;

        private PlayerController _hub;

        private void Awake()
        {
            _hub = GetComponentInParent<PlayerController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(PlayerController hub)
        {
            _hub = hub;
            if (_hub != null) _hub.RegisterModule(this);
        }

        public void OnRefreshModule() { }

        public void Die()
        {
            OnDeath?.Invoke();

            // Check for spawn controller (loot, etc.)
            if (TryGetComponent<SpawnController>(out var sc))
            {
                sc.TriggerDeath();
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
