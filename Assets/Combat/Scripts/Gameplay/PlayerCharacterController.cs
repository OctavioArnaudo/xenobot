using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Actúa como un puente de datos para la lógica de combate (armas, HUD).
    /// El movimiento y la cámara son manejados por un componente externo.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(AudioSource))]
    public class PlayerCharacterController : MonoBehaviour
    {
        [Header("References")]
        public Camera PlayerCamera;
        public AudioSource AudioSource;

        [Header("Combat Compatibility")]
        [Tooltip("Used by Jetpack for calculations")]
        public float GravityDownForce = 20f;
        [Tooltip("Used by WeaponsManager for weapon bobbing")]
        public float MaxSpeedOnGround = 10f;
        [Tooltip("Used by WeaponsManager for weapon bobbing")]
        public float SprintSpeedModifier = 2f;
        [Tooltip("Height at which the player dies instantly when falling off the map")]
        public float KillHeight = -50f;

        public UnityAction<bool> OnStanceChanged;

        // Propiedades requeridas por otros scripts (WeaponsManager, Jetpack)
        // Se mantiene como propiedad con getter y setter para compatibilidad con Jetpack.cs
        public Vector3 CharacterVelocity { get; set; }

        public bool IsGrounded => m_Controller != null && m_Controller.isGrounded;
        public bool HasJumpedThisFrame { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsCrouching { get; private set; }

        public float RotationMultiplier => 1f;

        Health m_Health;
        PlayerInputHandler m_InputHandler;
        CharacterController m_Controller;
        PlayerWeaponsManager m_WeaponsManager;

        void Awake()
        {
            // Ya no asignamos el jugador aquí para evitar el secuestro de referencia en red.
            // Se hará en el Start con validación de propiedad (Ownership).
        }

        void Start()
        {
            m_Controller = GetComponent<CharacterController>();
            m_InputHandler = GetComponent<PlayerInputHandler>();
            m_WeaponsManager = GetComponent<PlayerWeaponsManager>();
            m_Health = GetComponent<Health>();

            // Solo registrarse como el jugador principal si somos el dueño local
            NetworkObject netObj = GetComponent<NetworkObject>();
            if (netObj == null || netObj.IsOwner)
            {
                ActorsManager actorsManager = FindFirstObjectByType<ActorsManager>();
                if (actorsManager != null)
                    actorsManager.SetPlayer(gameObject);
            }

            if (PlayerCamera == null)
            {
                PlayerCamera = Camera.main;
            }

            if (m_Health) m_Health.OnDie += OnDie;
        }

        void Update()
        {
            if (IsDead) return;

            // Sincronizar velocidad con el controlador real para que el Weapon Bobbing funcione
            if (m_Controller != null)
            {
                // Solo actualizamos el valor para lectura de otros scripts.
                // No llamamos a m_Controller.Move aquí para evitar duplicar movimiento.
                CharacterVelocity = m_Controller.velocity;
            }

            // Detección de caída al vacío
            if (transform.position.y < KillHeight)
            {
                m_Health.Kill();
            }

            // Puente para el estado de salto (usado por Jetpack)
            HasJumpedThisFrame = m_InputHandler.GetJumpInputDown();

            // Manejo de agachado solo para estado/HUD (el movimiento lo hace el controlador externo)
            if (m_InputHandler.GetCrouchInputDown())
            {
                IsCrouching = !IsCrouching;
                OnStanceChanged?.Invoke(IsCrouching);
            }
        }

        void OnDie()
        {
            IsDead = true;
            if (m_WeaponsManager) m_WeaponsManager.SwitchToWeaponIndex(-1, true);
            EventManager.Broadcast(Events.PlayerDeathEvent);
        }

        // Método de compatibilidad para evitar errores en otros scripts
        public Vector3 GetDirectionReorientedOnSlope(Vector3 direction, Vector3 slopeNormal) => direction;
    }
}
