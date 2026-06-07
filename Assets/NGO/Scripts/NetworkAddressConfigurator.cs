using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace NGO.Networking
{
    /// <summary>
    /// Script modular para configurar la dirección IP y puerto en el transporte de Netcode.
    /// </summary>
    public class NetworkAddressConfigurator : MonoBehaviour
    {
        public void SetIPAddress(string ip, ushort port = 7777)
        {
            if (NetworkManager.Singleton == null) return;

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
                transport.SetConnectionData(ip, port, "0.0.0.0");
                Debug.Log($"[NetworkAddressConfigurator] IP configurada: {ip}:{port}");
            }
        }
    }
}
