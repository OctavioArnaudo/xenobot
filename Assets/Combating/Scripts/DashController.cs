using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Network-aware controller for directional dashing.
    /// Press Shift + W,A,S,D to trigger local client movement and sync animation/fuel via ServerRpc/ClientRpc.
    /// </summary>
    public class DashController : NetworkBehaviour
    {
        [Header("Dash Settings")]
        public float dashForce = 25f;
        public float dashDuration = 0.2f;
        public float dashCooldown = 1f;

        [Header("Stamina / Fuel Cost")]
        public float staminaCost = 20f;

        [Header("Animation Settings")]
        [SerializeField] private string dashTriggerName = "dash";

        private CharacterController m_CharController;
        private PlayerController m_Player;
        private HealthController m_Health;
        private PropulsionController m_Propulsion;
        private Animator m_Animator;

        private float m_DashTimer;
        private float m_NextDashTime;
        private Vector3 m_DashDirection;
        private bool m_IsDashing;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        void Start()
        {
            RefreshReferences();
        }

        public void ApplyEffect(GameObject player)
        {
            m_Player = player.GetComponent<PlayerController>();
            m_CharController = player.GetComponent<CharacterController>();
            m_Health = player.GetComponent<HealthController>();
            m_Propulsion = player.GetComponent<PropulsionController>() ?? player.GetComponentInChildren<PropulsionController>();
            RefreshReferences();
            Debug.Log("[DashController] Lógica de Dash de red activada para el jugador.");
        }

        private void RefreshReferences()
        {
            if (m_Player == null) m_Player = GetComponentInParent<PlayerController>();
            if (m_CharController == null) m_CharController = GetComponentInParent<CharacterController>();
            if (m_Health == null) m_Health = GetComponentInParent<HealthController>();
            if (m_Propulsion == null) m_Propulsion = GetComponent<PropulsionController>() ?? GetComponentInParent<PropulsionController>();

            if (m_Animator == null)
            {
                m_Animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>() ?? GetComponentInParent<Animator>();
                if (m_Animator == null && m_Player != null)
                {
                    m_Animator = m_Player.GetComponent<Animator>() ?? m_Player.GetComponentInChildren<Animator>();
                }
            }
        }

        void Update()
        {
            if (m_Player == null || m_CharController == null || m_Animator == null) RefreshReferences();
            if (m_Player == null || m_CharController == null) return;

            bool isOwner = IsNetworkActive ? IsOwner : true;
            if (!isOwner) return;

            if (InventoryController.LocalInstance != null && Cursor.visible) return;

            HandleDashExecution();

            if (!m_IsDashing)
            {
                DetectShiftDashInput();
            }
        }

        private void DetectShiftDashInput()
        {
            if (Keyboard.current == null || Time.time < m_NextDashTime) return;

            // Detecta la pulsación inicial de Shift (Izquierdo o Derecho)
            if (Keyboard.current.leftShiftKey.wasPressedThisFrame || Keyboard.current.rightShiftKey.wasPressedThisFrame)
            {
                PerformShiftDash();
            }
        }

        /// <summary>
        /// Calcula la dirección según el WASD presionado y ejecuta el Dash.
        /// </summary>
        public void PerformShiftDash()
        {
            if (Time.time < m_NextDashTime || m_IsDashing) return;

            float x = 0f;
            float z = 0f;

            if (Keyboard.current.wKey.isPressed) z += 1f;
            if (Keyboard.current.sKey.isPressed) z -= 1f;
            if (Keyboard.current.dKey.isPressed) x += 1f;
            if (Keyboard.current.aKey.isPressed) x -= 1f;

            Vector3 localDirection = new Vector3(x, 0f, z);

            // Si no hay ninguna tecla WASD presionada, hace el dash hacia adelante por defecto
            if (localDirection.sqrMagnitude < 0.01f)
            {
                localDirection = Vector3.forward;
            }
            else
            {
                localDirection.Normalize();
            }

            TryStartDash(localDirection);
        }

        private void TryStartDash(Vector3 localDirection)
        {
            Vector3 worldDirection = transform.TransformDirection(localDirection).normalized;

            if (IsNetworkActive)
            {
                RequestDashServerRpc(worldDirection);
            }
            else
            {
                ExecuteLocalDash(worldDirection);
            }
        }

        [ServerRpc]
        private void RequestDashServerRpc(Vector3 worldDirection)
        {
            if (m_Propulsion == null) RefreshReferences();
            if (m_Propulsion != null && staminaCost > 0)
            {
                if (m_Propulsion.JetpackFuel < staminaCost) return;
                m_Propulsion.UseFuel(staminaCost);
            }

            NotifyDashClientRpc(worldDirection);
        }

        [ClientRpc]
        private void NotifyDashClientRpc(Vector3 worldDirection)
        {
            if (IsOwner)
            {
                ExecuteLocalDash(worldDirection);
            }

            TriggerDashAnimation();
        }

        private void ExecuteLocalDash(Vector3 worldDirection)
        {
            m_DashDirection = worldDirection;
            m_IsDashing = true;
            m_DashTimer = dashDuration;
            m_NextDashTime = Time.time + dashCooldown;
        }

        private void HandleDashExecution()
        {
            if (!m_IsDashing) return;

            if (m_DashTimer > 0)
            {
                m_CharController.Move(m_DashDirection * dashForce * Time.deltaTime);
                m_DashTimer -= Time.deltaTime;
            }
            else
            {
                m_IsDashing = false;
            }
        }

        private void TriggerDashAnimation()
        {
            if (m_Animator == null) RefreshReferences();

            if (m_Animator != null && HasParameter(m_Animator, dashTriggerName))
            {
                m_Animator.SetTrigger(dashTriggerName);
            }
        }

        private bool HasParameter(Animator animator, string paramName)
        {
            foreach (AnimatorControllerParameter param in animator.parameters)
                if (param.name == paramName) return true;
            return false;
        }

        public bool IsDashing => m_IsDashing;
    }
}