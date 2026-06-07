using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace NGO.Networking
{
    /// <summary>
    /// Lógica modular y unitaria para las operaciones básicas de red.
    /// </summary>
    public static class NetworkingService
    {
        public static bool StartHost()
        {
            if (NetworkManager.Singleton == null) return false;
            return NetworkManager.Singleton.StartHost();
        }

        public static bool StartClient(string ip, ushort port = 7777)
        {
            if (NetworkManager.Singleton == null) return false;

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.SetConnectionData(ip, port);
            }

            return NetworkManager.Singleton.StartClient();
        }

        public static void Shutdown()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
    }
}
