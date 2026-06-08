using UnityEngine;
using Unity.Netcode;
using NGO.Gameplay.Base;

namespace NGO.Gameplay.Networking
{
    /// <summary>
    /// Script básico de gravedad para el Player Prefab inicial.
    /// Su única función es hacer que el jugador caiga hasta que toque un suelo
    /// que le inyecte un sistema de movimiento real.
    /// </summary>
    public class CoreGravity : NetworkBehaviour
    {
        private CharacterController _controller;
        private float _verticalVelocity;
        private PlayerSystemHub _hub;

        public override void OnNetworkSpawn()
        {
            _hub = GetComponent<PlayerSystemHub>();
            _controller = GetComponentInParent<CharacterController>();

            if (_controller == null)
            {
                Debug.LogError($"[CoreGravity] ERROR: No se encontró CharacterController en los padres de {gameObject.name}");
            }
            else
            {
                Debug.Log($"[CoreGravity] Inicializado correctamente en {gameObject.name}. IsOwner: {IsOwner}");
            }
        }

        private void Update()
        {
            // Verificación de seguridad y propiedad
            if (!IsOwner || _controller == null) return;

            // Si el Hub ya tiene un módulo de movimiento real, este script se desactiva
            if (_hub.GetModule<WalkPlayerController>() != null)
            {
                Debug.Log("[CoreGravity] Controlador de caminata detectado. Desactivando gravedad básica.");
                enabled = false;
                return;
            }

            // Aplicar caída constante para asegurar que baje
            _verticalVelocity += -15f * Time.deltaTime;

            // Limitar velocidad terminal de caída
            if (_verticalVelocity < -30f) _verticalVelocity = -30f;

            // Mover el controlador
            _controller.Move(new Vector3(0, _verticalVelocity, 0) * Time.deltaTime);

            // Log opcional para confirmar que el Update está corriendo
            // Debug.Log($"[CoreGravity] Cayendo... Velocidad: {_verticalVelocity}");
        }
    }
}
