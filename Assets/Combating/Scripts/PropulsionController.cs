using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Logic controller for jetpack flight and fuel storage.
    /// Handles fuel consumption, regeneration, capacity, and vertical movement.
    /// Fully compliant with AGENTS.md pattern (clean public overrides with dynamic environment-aware private defaults).
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
        public float maxFuel = 0f;
        public float jetpackForce = 0f;
        public float hoverForce = 0f;
        public float fuelConsumption = 0f;
        public float fuelRegen = 0f;
        public float maxUpwardVelocity = 0f;

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

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        // --- Effective Statistics Resolvers with AGENTS.md Protection ---

        public float EffectiveMaxFuel
        {
            get
            {
                try { if (maxFuel > 0f) return maxFuel; }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] maxFuel: {ex.Message}"); }

                var pc = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
                return (pc != null || CompareTag("Player")) ? DEFAULT_PLAYER_MAX_FUEL : DEFAULT_ENEMY_MAX_FUEL;
            }
        }

        public float EffectiveJetpackForce
        {
            get
            {
                try { if (jetpackForce > 0f) return jetpackForce; }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] jetpackForce: {ex.Message}"); }
                return DEFAULT_JETPACK_FORCE;
            }
        }

        public float EffectiveHoverForce
        {
            get
            {
                try { if (hoverForce > 0f) return hoverForce; }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] hoverForce: {ex.Message}"); }
                return DEFAULT_HOVER_FORCE;
            }
        }

        public float EffectiveFuelConsumption
        {
            get
            {
                try { if (fuelConsumption > 0f) return fuelConsumption; }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] fuelConsumption: {ex.Message}"); }
                return DEFAULT_FUEL_CONSUMPTION;
            }
        }

        public float EffectiveFuelRegen
        {
            get
            {
                try { if (fuelRegen > 0f) return fuelRegen; }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] fuelRegen: {ex.Message}"); }
                return DEFAULT_FUEL_REGEN;
            }
        }

        public float EffectiveMaxUpwardVelocity
        {
            get
            {
                try { if (maxUpwardVelocity > 0f) return maxUpwardVelocity; }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] maxUpwardVelocity: {ex.Message}"); }
                return 12f;
            }
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

            bool isBPressed = Keyboard.current != null && Keyboard.current.bKey.isPressed;

            m_IsUsingJetpack = false;
            if (isGrounded)
            {
                m_JetpackDepleted = false;
                if (allowRegen) AddFuel(EffectiveFuelRegen * Time.deltaTime);
                return false;
            }

            if (!isBPressed) m_JetpackDepleted = false;
            if (!infiniteFuel && JetpackFuel <= 0) m_JetpackDepleted = true;

            if (isBPressed && (infiniteFuel || (!m_JetpackDepleted && JetpackFuel > 0)))
            {
                m_IsUsingJetpack = true;
                if (verticalVelocity < -2f) verticalVelocity = Mathf.MoveTowards(verticalVelocity, 0, Time.deltaTime * 20f);
                float currentForce = (verticalVelocity > 0.5f) ? EffectiveHoverForce : EffectiveJetpackForce;
                verticalVelocity += currentForce * Time.deltaTime;
                if (verticalVelocity > EffectiveMaxUpwardVelocity) verticalVelocity = EffectiveMaxUpwardVelocity;

                if (!infiniteFuel) UseFuel(EffectiveFuelConsumption * Time.deltaTime);
            }
            else if (allowRegen) AddFuel((EffectiveFuelRegen * 0.2f) * Time.deltaTime);

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
