using UnityEngine;
using NGO.Gameplay.Base;
using Unity.Netcode;

namespace NGO.Gameplay.Networking
{
    public class MovementNetworking : MovementBase
    {
        private CharacterController _controller;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        public override void RequestMoveRpc(Vector3 direction)
        {
            // En un sistema real, aquí validaríamos la velocidad o usaríamos NetworkTransform.
            // Para este ejemplo modular:
            if (_controller != null)
            {
                _controller.Move(direction * Speed * Time.deltaTime);
            }
        }
    }
}
