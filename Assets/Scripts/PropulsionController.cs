using UnityEngine;
using Unity.Netcode;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Effective;
using Runtime.Scripts.Player;
using Runtime.Scripts.Health;
using Runtime.Scripts.Balance;


namespace Runtime.Scripts.Propulsion
{
    /// <summary>
    /// Logic controller for jetpack flight and fuel storage.
    /// Handles fuel consumption, regeneration, capacity, and vertical movement.
    /// </summary>
    public class PropulsionController : NetworkBehaviour
    {
        // --- Internal Hardcoded Fuel & Flight Defaults ---
        private const float DEFAULT_PLAYER_MAX_FUEL = 120f;
        private const float DEFAULT_ENEMY_MAX_FUEL = 80f;
        private const float DEFAULT_JETPACK_FORCE = 60f;
        private const float DEFAULT_HOVER_FORCE = 25f;
        private const float DEFAULT_FUEL_CONSUMPTION = 28f;
        private const float DEFAULT_FUEL_REGEN = 16f;

        [Header("Flight State Configuration")]
        public bool isUnlocked = false;   // Si está marcado, vuela desde el inicio. Si no, requiere jetpack.
        public bool infiniteFuel = false; // Combustible infinito
        public bool allowRegen = true;    // Recarga automática

        [Header("Manual Flight & Fuel Overrides")]
        public float MaxFuel;
        public Optional<float> maxFuel;
        public float JetpackForce;
        public Optional<float> jetpackForce;
        public float HoverForce;
        public Optional<float> hoverForce;
        public float FuelConsumption;
        public Optional<float> fuelConsumption;
        public float FuelRegen;
        public Optional<float> fuelRegen;
        public float MaxUpwardVelocity;
        public Optional<float> maxUpwardVelocity;

        [Header("Network Synchronized Data")]
        public NetworkVariable<float> maxJetpackFuel = new NetworkVariable<float>(120f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<float> currentJetpackFuel = new NetworkVariable<float>(120f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float m_OfflineJetpack = 120f;
        private float m_OfflineMaxJetpack = 120f;

        private CharacterController m_CharController;
        private PlayerController m_Player;
        private HealthController m_Health;
        private bool m_IsUsingJetpack = false;

        private static readonly int FlyingHash = Animator.StringToHash("isFlying");
        private Animator m_Anim;
        private bool m_AnimResolved;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        public float EffectiveMaxFuel
        {
            get
            {
                var pc = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
                float defaultFuel = (pc != null || CompareTag("Player")) ? DEFAULT_PLAYER_MAX_FUEL : DEFAULT_ENEMY_MAX_FUEL;
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return maxFuel.Get(MaxFuel, defaultFuel, stats.fuelCapacity);
            }
        }

        public float EffectiveJetpackForce => jetpackForce.Get(JetpackForce, DEFAULT_JETPACK_FORCE);
        public float EffectiveHoverForce => hoverForce.Get(HoverForce, DEFAULT_HOVER_FORCE);

        public float EffectiveFuelConsumption
        {
            get
            {
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return fuelConsumption.Get(FuelConsumption, DEFAULT_FUEL_CONSUMPTION, stats.jetpackConsumptionRate);
            }
        }

        public float EffectiveFuelRegen
        {
            get
            {
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return fuelRegen.Get(FuelRegen, DEFAULT_FUEL_REGEN, stats.jetpackRechargeRate);
            }
        }

        public float EffectiveMaxUpwardVelocity => maxUpwardVelocity.Get(MaxUpwardVelocity, 12f);

        private void UpdateInspectorValues()
        {
            MaxFuel = EffectiveMaxFuel;
            JetpackForce = EffectiveJetpackForce;
            HoverForce = EffectiveHoverForce;
            FuelConsumption = EffectiveFuelConsumption;
            FuelRegen = EffectiveFuelRegen;
            MaxUpwardVelocity = EffectiveMaxUpwardVelocity;
        }

        private void OnValidate()
        {
            UpdateInspectorValues();
        }

        public float JetpackFuel => IsNetworkActive ? currentJetpackFuel.Value : m_OfflineJetpack;
        public float MaxJetpack => IsNetworkActive ? maxJetpackFuel.Value : m_OfflineMaxJetpack;

        void Awake()
        {
            m_OfflineMaxJetpack = EffectiveMaxFuel;
            m_OfflineJetpack = m_OfflineMaxJetpack;
            RefreshReferences();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                maxJetpackFuel.Value = EffectiveMaxFuel;
                currentJetpackFuel.Value = maxJetpackFuel.Value;
            }
        }

        public void ApplyEffect(GameObject player)
        {
            m_Player = player.GetComponent<PlayerController>();
            m_Health = player.GetComponent<HealthController>();
            m_CharController = player.GetComponent<CharacterController>();
            RefreshReferences();
            Debug.Log("[PropulsionController] Lógica de vuelo activada para el jugador.");
        }

        private void RefreshReferences()
        {
            if (m_Player == null) m_Player = GetComponentInParent<PlayerController>();
            if (m_Health == null) m_Health = GetComponentInParent<HealthController>();
            if (m_CharController == null) m_CharController = GetComponentInParent<CharacterController>();
        }

        // --- Public Fuel Methods ---

        public void UseFuel(float amount)
        {
            if (amount <= 0f) return;

            if (IsNetworkActive)
            {
                if (IsServer) currentJetpackFuel.Value = Mathf.Max(0f, currentJetpackFuel.Value - amount);
                else UseFuelServerRpc(amount);
            }
            else
            {
                m_OfflineJetpack = Mathf.Max(0f, m_OfflineJetpack - amount);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void UseFuelServerRpc(float amount) => UseFuel(amount);

        public void AddFuel(float amount)
        {
            if (amount <= 0f) return;

            if (IsNetworkActive)
            {
                if (IsServer) currentJetpackFuel.Value = Mathf.Min(maxJetpackFuel.Value, currentJetpackFuel.Value + amount);
                else AddFuelServerRpc(amount);
            }
            else
            {
                m_OfflineJetpack = Mathf.Min(m_OfflineMaxJetpack, m_OfflineJetpack + amount);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void AddFuelServerRpc(float amount) => AddFuel(amount);

        public void UpgradeMaxFuel(float bonus)
        {
            if (IsNetworkActive)
            {
                if (IsServer)
                {
                    maxJetpackFuel.Value += bonus;
                    currentJetpackFuel.Value = Mathf.Min(maxJetpackFuel.Value, currentJetpackFuel.Value + bonus);
                }
            }
            else
            {
                m_OfflineMaxJetpack += bonus;
                AddFuel(bonus);
            }
        }

        /// <summary>Segundo salto en el aire: activa el jetpack si hay combustible. Si devuelve false, el doble salto normal sigue vigente.</summary>
        public bool TryStartFlight()
        {
            if (!isUnlocked) return false;
            if (m_Player == null) RefreshReferences();
            if (m_Player == null || (IsNetworkActive && !m_Player.IsOwner)) return false;
            if (!infiniteFuel && JetpackFuel <= 0f) return false;
            SetFlying(true);
            return true;
        }

        public bool ProcessFlight(bool isJumpHeld, bool isGrounded, ref float verticalVelocity)
        {
            if (!isUnlocked) return false;
            if (m_Player == null) RefreshReferences();
            if (m_Player == null || (IsNetworkActive && !m_Player.IsOwner)) return false;

            float dt = Time.deltaTime;

            if (isGrounded)
            {
                SetFlying(false);
                if (allowRegen) AddFuel(EffectiveFuelRegen * dt);
                return false;
            }

            if (m_IsUsingJetpack && (!isJumpHeld || (!infiniteFuel && JetpackFuel <= 0f))) SetFlying(false);

            if (!m_IsUsingJetpack)
            {
                if (allowRegen) AddFuel(EffectiveFuelRegen * 0.2f * dt);
                return false;
            }

            if (verticalVelocity < -2f) verticalVelocity = Mathf.MoveTowards(verticalVelocity, 0f, dt * 20f);
            float force = verticalVelocity > 0.5f ? EffectiveHoverForce : EffectiveJetpackForce;
            verticalVelocity = Mathf.Min(verticalVelocity + force * dt, EffectiveMaxUpwardVelocity);

            if (!infiniteFuel) UseFuel(EffectiveFuelConsumption * dt);
            return true;
        }

        private void SetFlying(bool on)
        {
            if (m_IsUsingJetpack == on) return;
            m_IsUsingJetpack = on;

            if (!m_AnimResolved)
            {
                m_AnimResolved = true;
                var anim = m_Player != null ? m_Player.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();
                if (anim != null)
                    foreach (var p in anim.parameters)
                        if (p.nameHash == FlyingHash) { m_Anim = anim; break; }
            }
            if (m_Anim != null) m_Anim.SetBool(FlyingHash, on);
        }

        public bool IsFlying => m_IsUsingJetpack;
    }
}