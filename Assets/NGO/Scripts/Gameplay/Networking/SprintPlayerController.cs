using UnityEngine;
using Unity.Netcode;
using NGO.Gameplay.Base;
using StarterAssets;

namespace NGO.Gameplay.Networking
{
    public class SprintPlayerController : PlayerActionController
    {
        [Header("Sprint Settings")]
        [SerializeField] private float sprintMultiplier = 2.0f;

        private WalkPlayerController m_WalkController;
        private StarterAssetsInputs m_Input;
        private bool m_IsSprinting;

        public override void Initialize(NetworkObject root)
        {
            base.Initialize(root);

            m_Input = root.GetComponentInChildren<StarterAssetsInputs>();

            var hub = root.GetComponentInChildren<PlayerSystemHub>();
            if (hub != null)
            {
                m_WalkController = hub.GetModule<WalkPlayerController>();
            }

            Debug.Log($"[Sprint] Inicializado. ¿Tengo WalkController?: {m_WalkController != null}");
        }

        public override void OnActionTriggered() { }

        public override void OnTick()
        {
            if (!IsOwner || m_WalkController == null || m_Input == null) return;

            if (m_Input.sprint)
            {
                if (!m_IsSprinting)
                {
                    m_IsSprinting = true;
                    Debug.Log("[Sprint] ¡Corriendo a toda velocidad!");
                }
                m_WalkController.SpeedMultiplier = sprintMultiplier;
            }
            else
            {
                if (m_IsSprinting)
                {
                    m_IsSprinting = false;
                    Debug.Log("[Sprint] Volviendo a caminata normal.");
                }
                m_WalkController.SpeedMultiplier = 1.0f;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (m_WalkController != null) m_WalkController.SpeedMultiplier = 1.0f;
            base.OnNetworkDespawn();
        }
    }
}
