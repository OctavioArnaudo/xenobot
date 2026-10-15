using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
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

        [Header("Control Settings")]
        [Tooltip("Tiempo en segundos que se debe mantener presionada la tecla Espacio en el aire para activar el jetpack.")]
        public float JetpackHoldThreshold = 0.25f;

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
        private bool m_JetpackDepleted = false;

        private float m_SpaceHoldTimer = 0f; // Temporizador para la pulsación sostenida de Espacio

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        public float EffectiveMaxFuel
        {
            get
            {
                try
                {
                    if (maxFuel.use) return maxFuel.value;
                    if (BalanceManager.Instance != null)
                    {
                        var stats = BalanceManager.Instance.GetEntityBalance(gameObject);
                        if (stats.fuelCapacity > 0) return stats.fuelCapacity;
                    }
                    var pc = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
                    float defaultFuel = (pc != null || CompareTag("Player")) ? DEFAULT_PLAYER_MAX_FUEL : DEFAULT_ENEMY_MAX_FUEL;
                    return maxFuel.GetValue(defaultFuel);
                }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] maxFuel: {ex.Message}"); }

                return DEFAULT_PLAYER_MAX_FUEL;
            }
        }

        public float EffectiveJetpackForce
        {
            get
            {
                try { return jetpackForce.GetValue(DEFAULT_JETPACK_FORCE); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] jetpackForce: {ex.Message}"); }
                return DEFAULT_JETPACK_FORCE;
            }
        }

        public float EffectiveHoverForce
        {
            get
            {
                try { return hoverForce.GetValue(DEFAULT_HOVER_FORCE); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] hoverForce: {ex.Message}"); }
                return DEFAULT_HOVER_FORCE;
            }
        }

        public float EffectiveFuelConsumption
        {
            get
            {
                try
                {
                    if (fuelConsumption.use) return fuelConsumption.value;
                    if (BalanceManager.Instance != null)
                    {
                        var stats = BalanceManager.Instance.GetEntityBalance(gameObject);
                        if (stats.jetpackConsumptionRate > 0) return stats.jetpackConsumptionRate;
                    }
                    return fuelConsumption.GetValue(DEFAULT_FUEL_CONSUMPTION);
                }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] fuelConsumption: {ex.Message}"); }
                return DEFAULT_FUEL_CONSUMPTION;
            }
        }

        public float EffectiveFuelRegen
        {
            get
            {
                try
                {
                    if (fuelRegen.use) return fuelRegen.value;
                    if (BalanceManager.Instance != null)
                    {
                        var stats = BalanceManager.Instance.GetEntityBalance(gameObject);
                        if (stats.jetpackRechargeRate > 0) return stats.jetpackRechargeRate;
                    }
                    return fuelRegen.GetValue(DEFAULT_FUEL_REGEN);
                }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] fuelRegen: {ex.Message}"); }
                return DEFAULT_FUEL_REGEN;
            }
        }

        public float EffectiveMaxUpwardVelocity
        {
            get
            {
                try { return maxUpwardVelocity.GetValue(12f); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] maxUpwardVelocity: {ex.Message}"); }
                return 12f;
            }
        }

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

        void Update()
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

        public bool ProcessFlight(bool isJumpHeld, bool isGrounded, ref float verticalVelocity)
        {
            if (!isUnlocked) return false;
            if (m_Player == null) RefreshReferences();
            if (m_Player == null) return false;

            bool isOwner = IsNetworkActive ? m_Player.IsOwner : true;
            if (!isOwner) return false;

            // Detectar la barra espaciadora
            bool isSpacePressed = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;

            m_IsUsingJetpack = false;

            // Si está en el suelo, reiniciar estados y recargar combustible
            if (isGrounded)
            {
                m_JetpackDepleted = false;
                m_SpaceHoldTimer = 0f;
                if (allowRegen) AddFuel(EffectiveFuelRegen * Time.deltaTime);
                return false;
            }

            // Calcular tiempo en que se mantiene presionado el Espacio en el aire
            if (isSpacePressed)
            {
                m_SpaceHoldTimer += Time.deltaTime;
            }
            else
            {
                m_SpaceHoldTimer = 0f;
                m_JetpackDepleted = false;
            }

            if (!infiniteFuel && JetpackFuel <= 0) m_JetpackDepleted = true;

            // Se activa solo si se mantiene Espacio por encima del umbral (ej. 0.25s) y hay combustible
            bool shouldFly = isSpacePressed && m_SpaceHoldTimer >= JetpackHoldThreshold;

            if (shouldFly && (infiniteFuel || (!m_JetpackDepleted && JetpackFuel > 0)))
            {
                m_IsUsingJetpack = true;
                if (verticalVelocity < -2f) verticalVelocity = Mathf.MoveTowards(verticalVelocity, 0, Time.deltaTime * 20f);
                float currentForce = (verticalVelocity > 0.5f) ? EffectiveHoverForce : EffectiveJetpackForce;
                verticalVelocity += currentForce * Time.deltaTime;
                if (verticalVelocity > EffectiveMaxUpwardVelocity) verticalVelocity = EffectiveMaxUpwardVelocity;

                if (!infiniteFuel) UseFuel(EffectiveFuelConsumption * Time.deltaTime);
            }
            else if (allowRegen)
            {
                AddFuel((EffectiveFuelRegen * 0.2f) * Time.deltaTime);
            }

            Animator anim = m_Player != null ? m_Player.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();
            if (anim != null && HasParameter(anim, "isFlying"))
            {
                anim.SetBool("isFlying", m_IsUsingJetpack);
            }

            return m_IsUsingJetpack;
        }

        private bool HasParameter(Animator animator, string paramName)
        {
            foreach (AnimatorControllerParameter param in animator.parameters)
                if (param.name == paramName) return true;
            return false;
        }

        public bool IsFlying => m_IsUsingJetpack;
    }
}