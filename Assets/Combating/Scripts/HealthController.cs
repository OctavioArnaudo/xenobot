using UnityEngine;
using Unity.Netcode;
using UnityEngine.Events;

namespace Combating.Scripts
{
    public enum Team { Neutral, Player, Enemy }

    /// <summary>
    /// Universal controller for Health and Team.
    /// Handles life, damage and status.
    /// </summary>
    public class HealthController : NetworkBehaviour
    {
        [Header("Identity & Team")]
        public Team team = Team.Neutral;
        public int maxHealth = 100;

        [Header("Jetpack Settings")]
        public float maxJetpack = 0f;

        [Header("Events")]
        public UnityEvent OnDeath;
        public UnityEvent<int> OnTakeDamage;

        private NetworkVariable<int> currentHealth = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private int m_OfflineHealth;
        private float m_Jetpack;
        private float m_DamageFlashTimer;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
        public int CurrentHP => IsNetworkActive ? currentHealth.Value : m_OfflineHealth;
        public float JetpackFuel => m_Jetpack;

        void Awake()
        {
            m_OfflineHealth = maxHealth;
            m_Jetpack = maxJetpack;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) currentHealth.Value = maxHealth;
            m_Jetpack = maxJetpack;
        }

        void Update()
        {
            if (m_DamageFlashTimer > 0) m_DamageFlashTimer -= Time.deltaTime;
        }

        public void TakeDamage(int damage)
        {
            if (damage <= 0) return;
            if (IsNetworkActive) { if (IsServer) currentHealth.Value = Mathf.Max(0, currentHealth.Value - damage); }
            else m_OfflineHealth = Mathf.Max(0, m_OfflineHealth - damage);

            if (IsOwner && team == Team.Player) m_DamageFlashTimer = 0.6f;

            OnTakeDamage?.Invoke(damage);
            if (CurrentHP <= 0) Die();
        }

        public void UseFuel(float amount) => m_Jetpack = Mathf.Max(0f, m_Jetpack - amount);
        public void AddFuel(float amount) => m_Jetpack = Mathf.Min(maxJetpack, m_Jetpack + amount);

        private void Die()
        {
            OnDeath?.Invoke();
            if (TryGetComponent<SpawnController>(out var sc)) sc.TriggerDeath();
            else
            {
                if (IsNetworkActive && IsServer && IsSpawned)
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

        #region UI Effects
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return; // Optimizacion: Solo procesar en el dibujo
            if (!IsOwner || team != Team.Player) return;

            float sw = Screen.width;
            float sh = Screen.height;

            // Flash de Daño
            if (m_DamageFlashTimer > 0)
            {
                GUI.color = new Color(1, 0, 0, m_DamageFlashTimer * 0.7f);
                GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            // Integridad Crítica
            if (CurrentHP < maxHealth * 0.25f && CurrentHP > 0)
            {
                GUI.color = new Color(1, 0, 0, Mathf.PingPong(Time.time * 4f, 0.4f));
                GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
                GUI.color = Color.white;

                GUIStyle style = new GUIStyle();
                style.alignment = TextAnchor.MiddleCenter;
                style.fontSize = 26;
                style.fontStyle = FontStyle.Bold;
                style.normal.textColor = Color.white;
                GUI.Label(new Rect(0, sh / 2 + 100, sw, 50), "!!! ADVERTENCIA: INTEGRIDAD CRÍTICA !!!", style);
            }
        }
        #endregion
    }
}
