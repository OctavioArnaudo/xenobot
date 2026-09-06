using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;
using Combating.Scripts;

namespace Combating.Scripts
{
    public class LevelingController : MonoBehaviour, IModular
    {
        // Leveling Reliability Constants
        private const float ATTACK_GROWTH = 2.0f;
        private const float DEFENSE_GROWTH = 1.5f;
        private const float EXP_GROWTH_FACTOR = 1.2f;
        private const float INITIAL_EXP_REQUIRED = 100f;

        private ModularController _hub;

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);

                // ONLY if strictly offline, initialize stats here.
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                {
                    InitializeStats();
                }

                OnRefreshModule();
            }
        }

        public void OnRefreshModule() { }

        public void InitializeStats()
        {
            if (_hub == null) return;

            // Offline or server-side initialization
            _hub.Attack.Value = Random.Range(5f, 15f);
            _hub.Defense.Value = Random.Range(3f, 10f);
            _hub.ExpToLevelUp.Value = INITIAL_EXP_REQUIRED;
        }

        public void AddExp(float amount)
        {
            if (_hub == null) return;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                // Delegate to Hub ServerRpc
                _hub.AddExpServerRpc(amount);
            }
            else
            {
                InternalAddExp(amount);
            }
        }

        private void InternalAddExp(float amount)
        {
            if (_hub == null) return;

            _hub.Exp.Value += amount;
            while (_hub.Exp.Value >= _hub.ExpToLevelUp.Value)
            {
                _hub.Exp.Value -= _hub.ExpToLevelUp.Value;
                LevelUp();
            }
        }

        private void LevelUp()
        {
            if (_hub == null) return;

            _hub.Level.Value++;
            _hub.Attack.Value += ATTACK_GROWTH;
            _hub.Defense.Value += DEFENSE_GROWTH;
            _hub.ExpToLevelUp.Value *= EXP_GROWTH_FACTOR;

            // Benefits
            var hp = _hub.GetModule<HealthController>();
            if (hp != null) _hub.maxHealth.Value += 15;

            var heal = _hub.GetModule<HealController>();
            if (heal != null) heal.Heal(15);

            var tank = _hub.GetModule<TankController>();
            if (tank != null) _hub.maxFuel.Value += 20f;
        }
    }
}
