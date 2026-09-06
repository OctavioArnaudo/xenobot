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
        private HealthController _health;
        private bool _isDead = false;

        private void Awake()
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
                _health = _hub.GetModule<HealthController>();
            }
        }

        private void Update()
        {
            if (_isDead) return;

            // Only server or local authority should decide death in a networked environment
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (!IsServer) return;
            }

            if (_health != null && _health.CurrentHP <= 0)
            {
                Die();
            }
        }

        public void Die()
        {
            if (_isDead) return;
            _isDead = true;
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
