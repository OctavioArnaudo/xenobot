using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public enum Team { Neutral, Player, Enemy }

    public class HealthController : NetworkBehaviour, IModular
    {
        [Header("Identity & Team")]
        public Team team = Team.Neutral;
        public int maxHealth = 100;

        [Header("Animation")]
        public Animator animator;

        private NetworkVariable<int> currentHealth = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private int m_OfflineHealth;
        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        public int CurrentHP => IsNetworkActive ? currentHealth.Value : m_OfflineHealth;

        private static readonly int _animIDTakeDamage = Animator.StringToHash("takeDamage");
        private bool _hasAnimator;
        private bool _hasAnimIDTakeDamage;

        private ModularController _hub;

        void Awake()
        {
            m_OfflineHealth = maxHealth;
            _hub = GetComponentInParent<ModularController>();
            if (_hub != null) Bind(_hub);
            RefreshAnimatorReference();
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
            RefreshAnimatorReference();
        }

        private void RefreshAnimatorReference()
        {
            if (animator == null) animator = (_hub != null) ? _hub.animator : GetComponentInChildren<Animator>();
            _hasAnimator = animator != null;
            if (_hasAnimator) _hasAnimIDTakeDamage = HasParameter(animator, _animIDTakeDamage);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) currentHealth.Value = maxHealth;
        }

        public void ApplyDirectHealthChange(int amount)
        {
            if (IsNetworkActive)
            {
                if (IsServer) currentHealth.Value = Mathf.Clamp(currentHealth.Value + amount, 0, maxHealth);
            }
            else
            {
                m_OfflineHealth = Mathf.Clamp(m_OfflineHealth + amount, 0, maxHealth);
            }

            if (amount < 0) TriggerTakeDamageAnimation();
        }

        private void TriggerTakeDamageAnimation()
        {
            if (!_hasAnimator || animator == null)
            {
                RefreshAnimatorReference();
                if (!_hasAnimator || animator == null) return;
            }

            if (_hasAnimIDTakeDamage) animator.SetTrigger(_animIDTakeDamage);
        }

        public void Heal(int amount)
        {
            if (amount <= 0) return;
            ApplyDirectHealthChange(amount);
        }

        public void UpgradeMaxHealth(int bonus)
        {
            maxHealth += bonus;
            ApplyDirectHealthChange(bonus);
        }

        private bool HasParameter(Animator anim, int paramHash)
        {
            if (anim == null) return false;
            foreach (AnimatorControllerParameter param in anim.parameters)
                if (param.nameHash == paramHash) return true;
            return false;
        }
    }
}
