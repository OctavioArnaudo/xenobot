using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// Sistema de escudo activado por la tecla Intro (Enter).
    /// Mitiga el daño entrante y activa efectos visuales/sonoros.
    /// </summary>
    public class ShieldController : NetworkBehaviour
    {
        [Header("Shield Settings")]
        [Tooltip("1.0 = Bloqueo total (100%), 0.5 = Mitiga el 50% del daño")]
        [Range(0f, 1f)]
        public float damageReduction = 1.0f;

        [Header("Visuals & Audio")]
        public GameObject shieldVisualObject;
        public AudioClip shieldActivateSound;
        public AudioClip shieldBlockSound;

        [Header("Animation")]
        public string shieldAnimBool = "isShieldActive";

        private Animator m_Animator;

        // NetworkVariable para sincronizar con otros jugadores en multijugador
        private readonly NetworkVariable<bool> m_IsShieldActive = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        // Estado local para cuando se prueba sin red (offline)
        private bool m_OfflineShieldActive = false;

        // Propiedad que devuelve el estado actual (sea online u offline)
        public bool IsShieldActive => IsNetworkActive ? m_IsShieldActive.Value : m_OfflineShieldActive;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        private void Awake()
        {
            m_Animator = GetComponentInChildren<Animator>();

            if (shieldVisualObject != null)
                shieldVisualObject.SetActive(false);
        }

        private void Update()
        {
            // Solo el dueño del jugador procesa su input local
            if (IsNetworkActive && !IsOwner) return;

            if (Cursor.visible)
            {
                SetShieldState(false);
                return;
            }

            // Detección de la tecla Enter (Intro principal o Numpad Enter)
            bool isHoldingEnter = Keyboard.current != null &&
                                  (Keyboard.current.enterKey.isPressed || Keyboard.current.numpadEnterKey.isPressed);

            SetShieldState(isHoldingEnter);
        }

        private void SetShieldState(bool active)
        {
            // Si el estado no cambió, no hacemos nada
            if (IsShieldActive == active) return;

            if (IsNetworkActive)
            {
                m_IsShieldActive.Value = active;
            }
            else
            {
                // Modo offline: guardamos el cambio localmente y actualizamos visuales
                bool previous = m_OfflineShieldActive;
                m_OfflineShieldActive = active;
                OnShieldStateChanged(previous, active);
            }
        }

        public override void OnNetworkSpawn()
        {
            m_IsShieldActive.OnValueChanged += OnShieldStateChanged;
        }

        public override void OnNetworkDespawn()
        {
            m_IsShieldActive.OnValueChanged -= OnShieldStateChanged;
        }

        private void OnShieldStateChanged(bool previousValue, bool newValue)
        {
            // Activar o desactivar el objeto visual del escudo
            if (shieldVisualObject != null)
            {
                shieldVisualObject.SetActive(newValue);
            }

            // Reproducir sonido solo al encender
            if (newValue && shieldActivateSound != null)
            {
                AudioSource.PlayClipAtPoint(shieldActivateSound, transform.position);
            }

            // Actualizar el Animator
            if (m_Animator != null && HasParameter(m_Animator, shieldAnimBool))
            {
                m_Animator.SetBool(shieldAnimBool, newValue);
            }
        }

        /// <summary>
        /// Procesa el daño entrante aplicando la reducción configurada si el escudo está activo.
        /// </summary>
        public float ProcessIncomingDamage(float damage)
        {
            if (!IsShieldActive) return damage;

            if (shieldBlockSound != null)
            {
                AudioSource.PlayClipAtPoint(shieldBlockSound, transform.position);
            }

            return damage * (1f - damageReduction);
        }

        private bool HasParameter(Animator animator, string paramName)
        {
            foreach (AnimatorControllerParameter param in animator.parameters)
                if (param.name == paramName) return true;
            return false;
        }
    }
}