using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Network-aware controller for directional dashing.
    /// Press and release Shift quickly (Tap) + W,A,S,D to trigger local client movement and sync animation/fuel via ServerRpc/ClientRpc.
    /// </summary>
    public class DashController : NetworkBehaviour
    {
        [Header("Dash Settings")]
        public float dashForce = 25f;
        public float dashDuration = 0.2f;
        public float dashCooldown = 1f;

        [Header("Tap / Hold Settings")]
        [Tooltip("Tiempo máximo en segundos que debe durar la pulsación de Shift para considerarse un Tap.")]
        [SerializeField] private float tapThreshold = 0.2f;

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

        // Variables para control de Tap vs Hold
        private float m_ShiftPressStartTime;
        private bool m_IsWaitingForShiftRelease;

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

            var leftShift = Keyboard.current.leftShiftKey;
            var rightShift = Keyboard.current.rightShiftKey;

            // 1. Detectar cuando se presiona Shift por primera vez
            if (leftShift.wasPressedThisFrame || rightShift.wasPressedThisFrame)
            {
                m_ShiftPressStartTime = Time.time;
                m_IsWaitingForShiftRelease = true;
            }

            // 2. Si se mantiene presionado más allá del tiempo de Tap, se cancela el Dash (Hold)
            if (m_IsWaitingForShiftRelease && (Time.time - m_ShiftPressStartTime) > tapThreshold)
            {
                m_IsWaitingForShiftRelease = false;
            }

            // 3. Al soltar Shift, se evalúa si fue Tap y si el jugador se está moviendo con WASD
            if (m_IsWaitingForShiftRelease && (leftShift.wasReleasedThisFrame || rightShift.wasReleasedThisFrame))
            {
                m_IsWaitingForShiftRelease = false;

                if (IsPlayerMoving())
                {
                    PerformShiftDash();
                }
            }
        }

        /// <summary>
        /// Comprueba si hay entrada activa de movimiento en WASD.
        /// </summary>
        private bool IsPlayerMoving()
        {
            if (Keyboard.current == null) return false;

            return Keyboard.current.wKey.isPressed ||
                   Keyboard.current.sKey.isPressed ||
                   Keyboard.current.aKey.isPressed ||
                   Keyboard.current.dKey.isPressed;
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