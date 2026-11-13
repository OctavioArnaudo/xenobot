using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Runtime.Scripts.Player;
using Runtime.Scripts.Health;
using Runtime.Scripts.Propulsion;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Effective;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Inventory;

namespace Runtime.Scripts.Singleton
{
    /// <summary>
    /// Network-aware controller for directional dashing.
    /// Press and release Shift quickly (Tap) + W,A,S,D to trigger local client movement and sync animation via ServerRpc/ClientRpc.
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

        [Header("Animation Settings")]
        [SerializeField] private string dashTriggerName = "dash";
        [Tooltip("Asigna aquí el Animator del modelo del jugador. Si está vacío, se buscará automáticamente.")]
        [SerializeField] private Animator m_Animator;

        private CharacterController m_CharController;
        private PlayerController m_Player;
        private HealthController m_Health;
        private PropulsionController m_Propulsion;

        private float m_DashTimer;
        private float m_NextDashTime;
        private Vector3 m_DashDirection;
        private bool m_IsDashing;

        // Control de Tap vs Hold
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
            m_Propulsion = player.GetComponent<PropulsionController>() ?? player.GetComponentInChildren<PropulsionController>(true);
            RefreshReferences();
            Debug.Log("[DashController] Lógica de Dash de red activada para el jugador.");
        }

        private void RefreshReferences()
        {
            if (m_Player == null) m_Player = GetComponentInParent<PlayerController>() ?? GetComponentInChildren<PlayerController>(true);
            if (m_CharController == null) m_CharController = GetComponentInParent<CharacterController>() ?? GetComponentInChildren<CharacterController>(true);
            if (m_Health == null) m_Health = GetComponentInParent<HealthController>() ?? GetComponentInChildren<HealthController>(true);
            if (m_Propulsion == null) m_Propulsion = GetComponent<PropulsionController>() ?? GetComponentInParent<PropulsionController>() ?? GetComponentInChildren<PropulsionController>(true);

            if (m_Animator == null)
            {
                // 1. Buscar en el mismo objeto, hijos (incluso inactivos) o padres
                m_Animator = GetComponent<Animator>() ??
                           GetComponentInChildren<Animator>(true) ??
                           GetComponentInParent<Animator>();

                // 2. Si no lo encuentra, buscar desde el GameObject del PlayerController
                if (m_Animator == null && m_Player != null)
                {
                    m_Animator = m_Player.GetComponent<Animator>() ??
                                 m_Player.GetComponentInChildren<Animator>(true);
                }

                // 3. Como último recurso, buscar en toda la jerarquía raíz del prefab
                if (m_Animator == null)
                {
                    m_Animator = transform.root.GetComponentInChildren<Animator>(true);
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

            // 2. Si se mantiene presionado más allá del umbral, se cancela el Dash (se considera Hold)
            if (m_IsWaitingForShiftRelease && (Time.time - m_ShiftPressStartTime) > tapThreshold)
            {
                m_IsWaitingForShiftRelease = false;
            }

            // 3. Al soltar Shift dentro de la ventana de tiempo
            if (m_IsWaitingForShiftRelease && (leftShift.wasReleasedThisFrame || rightShift.wasReleasedThisFrame))
            {
                m_IsWaitingForShiftRelease = false;

                if (IsPlayerMoving())
                {
                    PerformShiftDash();
                }
                else
                {
                    Debug.LogWarning("[DashController] Se soltó Shift a tiempo, pero NO hay teclas WASD presionadas.");
                }
            }
        }

        private bool IsPlayerMoving()
        {
            if (Keyboard.current == null) return false;

            return Keyboard.current.wKey.isPressed ||
                   Keyboard.current.sKey.isPressed ||
                   Keyboard.current.aKey.isPressed ||
                   Keyboard.current.dKey.isPressed;
        }

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
            // El dash usa los ejes horizontales de la cámara, igual que el movimiento del jugador.
            Camera mainCamera = Camera.main;
            float cameraYaw = mainCamera != null
                ? mainCamera.transform.eulerAngles.y
                : (m_Player != null ? m_Player.transform.eulerAngles.y : transform.eulerAngles.y);
            Vector3 worldDirection = Quaternion.Euler(0f, cameraYaw, 0f) * localDirection;
            worldDirection.y = 0f;
            worldDirection.Normalize();

            TriggerDashAnimation();

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
            NotifyDashClientRpc(worldDirection);
        }

        [ClientRpc]
        private void NotifyDashClientRpc(Vector3 worldDirection)
        {
            if (IsOwner)
            {
                ExecuteLocalDash(worldDirection);
            }
            else
            {
                TriggerDashAnimation();
            }
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
                Vector3 moveVector = m_DashDirection * dashForce;

                // Anclar al suelo si ya estaba apoyado para que CharacterController.isGrounded no pase a false
                if (m_CharController.isGrounded)
                {
                    moveVector.y = -4f;
                }

                m_CharController.Move(moveVector * Time.deltaTime);
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

            if (m_Animator == null)
            {
                Debug.LogError("[DashController] ERROR: m_Animator es NULL. Arrastra el Animator manualmente en el Inspector del DashController.");
                return;
            }

            if (!m_Animator.gameObject.activeInHierarchy || !m_Animator.enabled)
            {
                Debug.LogWarning($"[DashController] ADVERTENCIA: El Animator en '{m_Animator.gameObject.name}' está desactivado.");
                return;
            }

            if (m_Animator.runtimeAnimatorController == null)
            {
                Debug.LogError($"[DashController] ERROR: El Animator en '{m_Animator.gameObject.name}' no tiene asignado ningún AnimatorController.");
                return;
            }

            if (!HasParameter(m_Animator, dashTriggerName))
            {
                Debug.LogError($"[DashController] ERROR: El Animator '{m_Animator.runtimeAnimatorController.name}' no contiene ningún parámetro llamado '{dashTriggerName}'.");
                return;
            }

            m_Animator.SetTrigger(dashTriggerName);
            Debug.Log($"[DashController] -> SetTrigger('{dashTriggerName}') ejecutado en '{m_Animator.gameObject.name}'.");
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