using Unity.Netcode;
using UnityEngine;
using Unity.Collections;

namespace NGO.Networking
{
    /// <summary>
    /// Sincroniza la identidad del jugador (Nombre, Color e ID) a través de la red.
    /// Este script debe ir en el Prefab del Jugador.
    /// </summary>
    public class NetworkPlayerIdentity : NetworkBehaviour
    {
        [Header("Variables Sincronizadas")]
        public NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(
            new FixedString32Bytes(""), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public NetworkVariable<Color> playerColor = new NetworkVariable<Color>(
            Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public NetworkVariable<int> playerCustomID = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [Header("Referencias Visuales")]
        [SerializeField] private Renderer outlineRenderer;
        [SerializeField] private TMPro.TMP_Text nameTagText;

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                // Al spawnear, el dueño aplica sus configuraciones locales guardadas en el menú
                playerName.Value = LocalUserConfig.UserName;
                playerColor.Value = LocalUserConfig.UserColor;
                playerCustomID.Value = LocalUserConfig.UserCustomID;
            }

            // Suscribirse a cambios para actualizar la visual en todos los clientes
            playerName.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
            playerColor.OnValueChanged += (oldVal, newVal) => UpdateVisuals();

            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (nameTagText != null)
            {
                nameTagText.text = playerName.Value.ToString();
            }

            if (outlineRenderer != null)
            {
                // Asumiendo que el material tiene una propiedad de color para el contorno o base
                outlineRenderer.material.color = playerColor.Value;
            }
        }
    }
}
