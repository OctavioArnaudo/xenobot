using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class PropulsionController : MonoBehaviour, IItemFunctional, IModular
    {
        [Header("Flight Settings")]
        public float jetpackForce = 60f;
        public float hoverForce = 25f;
        public float fuelConsumption = 30f;
        public float fuelRegen = 15f;
        public float maxUpwardVelocity = 12f;
        public float hoverThreshold = 0.5f;

        private TankController m_Tank;
        private CharacterController m_CharController;
        private ModularController _hub;
        private bool m_IsUsingJetpack = false;
        private bool m_JetpackDepleted = false;

        private void Awake()
        {
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
        }

        public void ApplyEffect(GameObject player)
        {
            _hub = player.GetComponent<ModularController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);

                if (_hub is PlayerController) enabled = false;
                else if (_hub is EnemyController) enabled = true;

                OnRefreshModule();
            }
        }

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                m_Tank = _hub.GetModule<TankController>();
                m_CharController = _hub.controller ?? _hub.GetComponent<CharacterController>();
            }
        }

        public bool ProcessFlight(bool isJumpHeld, bool isGrounded, ref float verticalVelocity)
        {
            if (_hub == null) return false;

            bool isOwner = (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || _hub.IsOwner);
            if (!isOwner) return false;

            m_IsUsingJetpack = false;
            if (isGrounded)
            {
                m_JetpackDepleted = false;
                if (m_Tank != null) m_Tank.AddFuel(fuelRegen * Time.deltaTime);
                return false;
            }

            if (!isJumpHeld) m_JetpackDepleted = false;
            if (m_Tank != null && m_Tank.JetpackFuel <= 0) m_JetpackDepleted = true;

            if (isJumpHeld && !m_JetpackDepleted && m_Tank != null && m_Tank.JetpackFuel > 0)
            {
                m_IsUsingJetpack = true;
                if (verticalVelocity < -2f) verticalVelocity = Mathf.MoveTowards(verticalVelocity, 0, Time.deltaTime * 20f);
                float currentForce = (verticalVelocity > hoverThreshold) ? hoverForce : jetpackForce;
                verticalVelocity += currentForce * Time.deltaTime;
                if (verticalVelocity > maxUpwardVelocity) verticalVelocity = maxUpwardVelocity;
                m_Tank.UseFuel(fuelConsumption * Time.deltaTime);
            }
            else if (m_Tank != null) m_Tank.AddFuel((fuelRegen * 0.2f) * Time.deltaTime);

            return m_IsUsingJetpack;
        }

        public bool IsFlying => m_IsUsingJetpack;
    }
}
