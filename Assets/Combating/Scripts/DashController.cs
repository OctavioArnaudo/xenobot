using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Network-aware controller for directional dashing.
    /// Double-tap W,A,S,D triggers local client movement and syncs animation/fuel via ServerRpc/ClientRpc.
    /// </summary>
    public class DashController : NetworkBehaviour
    {
        [Header("Dash Settings")]
        public float dashForce = 25f;
        public float dashDuration = 0.2f;
        public float dashCooldown = 1f;
        public float doubleTapTimeWindow = 0.25f;

        [Header("Stamina / Fuel Cost")]
        public float staminaCost = 20f;

        private CharacterController m_CharController;
        private PlayerController m_Player;
        private HealthController m_Health;
        private PropulsionController m_Propulsion;

        private float m_LastTapTimeW;
        private float m_LastTapTimeA;
        private float m_LastTapTimeS;
        private float m_LastTapTimeD;

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
        }

        void Update()
        {
            if (m_Player == null || m_CharController == null) RefreshReferences();
            if (m_Player == null || m_CharController == null) return;

            bool isOwner = IsNetworkActive ? IsOwner : true;
            if (!isOwner) return;

            if (InventoryController.LocalInstance != null && Cursor.visible) return;

            HandleDashExecution();

            if (!m_IsDashing)
            {
                DetectDoubleTapInput();
            }
        }

        private void DetectDoubleTapInput()
        {
            if (Keyboard.current == null || Time.time < m_NextDashTime) return;

            if (Keyboard.current.wKey.wasPressedThisFrame)
                CheckDoubleTap(ref m_LastTapTimeW, Vector3.forward);

            else if (Keyboard.current.aKey.wasPressedThisFrame)
                CheckDoubleTap(ref m_LastTapTimeA, Vector3.left);

            else if (Keyboard.current.sKey.wasPressedThisFrame)
                CheckDoubleTap(ref m_LastTapTimeS, Vector3.back);

            else if (Keyboard.current.dKey.wasPressedThisFrame)
                CheckDoubleTap(ref m_LastTapTimeD, Vector3.right);
        }

        private void CheckDoubleTap(ref float lastTapTime, Vector3 localDirection)
        {
            if (Time.time - lastTapTime <= doubleTapTimeWindow)
            {
                TryStartDash(localDirection);
            }
            lastTapTime = Time.time;
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
            Animator anim = m_Player != null ? m_Player.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();
            if (anim != null && HasParameter(anim, "dash"))
            {
                anim.SetTrigger("dash");
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
