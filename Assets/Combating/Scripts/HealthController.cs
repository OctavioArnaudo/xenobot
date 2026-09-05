using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;
namespace Combating.Scripts {
    public enum Team { Neutral, Player, Enemy }
    /// <summary>
    /// Specialized controller for character Health state and Team identity.
    /// Acts as the data source for life status.
    /// </summary>
    public class HealthController : NetworkBehaviour, IPlayer {
        [Header("Identity & Team")]
        public Team team = Team.Neutral;
        public int maxHealth = 100;

        [Header("Animation")]
        public Animator animator;

        private NetworkVariable<int> currentHealth = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private int m_OfflineHealth;
        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        public int CurrentHP => IsNetworkActive ? currentHealth.Value : m_OfflineHealth;

        // Debe coincidir EXACTAMENTE con el parámetro Trigger "takeDamage" del Animator Controller
        private static readonly int _animIDTakeDamage = Animator.StringToHash("takeDamage");
        private bool _hasAnimator;
        private bool _hasAnimIDTakeDamage;

        void Awake() {
            m_OfflineHealth = maxHealth;
            var hub = GetComponentInParent<PlayerController>();
            if (hub != null) Bind(hub);
            RefreshAnimatorReference();
        }

        public void Bind(PlayerController hub) {
            if (hub != null) hub.RegisterModule(this);
        }

        public void OnRefreshModule() {
            RefreshAnimatorReference();
        }

        /// <summary>
        /// Busca y cachea el Animator del jugador/enemigo y verifica si tiene el parámetro "takeDamage".
        /// </summary>
        private void RefreshAnimatorReference() {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _hasAnimator = animator != null;
            if (_hasAnimator) _hasAnimIDTakeDamage = HasParameter(animator, _animIDTakeDamage);
        }

        public override void OnNetworkSpawn() {
            if (IsServer) currentHealth.Value = maxHealth;
        }

        public void ApplyDirectHealthChange(int amount) {
            if (IsNetworkActive) {
                if (IsServer) currentHealth.Value = Mathf.Clamp(currentHealth.Value + amount, 0, maxHealth);
            }
            else {
                m_OfflineHealth = Mathf.Clamp(m_OfflineHealth + amount, 0, maxHealth);
            }

            // Un "amount" negativo representa daño recibido; uno positivo es curación (no dispara la animación de daño).
            if (amount < 0) TriggerTakeDamageAnimation();
        }

        /// <summary>
        /// Dispara el Trigger "takeDamage" en el Animator al recibir daño.
        /// </summary>
        private void TriggerTakeDamageAnimation() {
            if (!_hasAnimator || animator == null) {
                RefreshAnimatorReference();
                if (!_hasAnimator || animator == null) return;
            }

            if (_hasAnimIDTakeDamage) animator.SetTrigger(_animIDTakeDamage);
        }

        public void Heal(int amount) {
            if (amount <= 0) return;
            ApplyDirectHealthChange(amount);
            Debug.Log($"[Health] Recuperada {amount} HP. Vida actual: {CurrentHP}");
        }

        public void UpgradeMaxHealth(int bonus) {
            maxHealth += bonus;
            ApplyDirectHealthChange(bonus);
        }

        private bool HasParameter(Animator anim, int paramHash) {
            if (anim == null) return false;
            foreach (AnimatorControllerParameter param in anim.parameters)
                if (param.nameHash == paramHash) return true;
            return false;
        }
    }
}
