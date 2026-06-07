using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace NGO.Networking
{
    public static class NetworkingService
    {
        /// <summary>
        /// Inicia el Host asegurando que escuche en todas las interfaces (0.0.0.0)
        /// para permitir conexiones entrantes.
        /// </summary>
        /// <summary>
        /// Inicia el Host.
        /// </summary>
        /// <param name="port">Puerto local</param>
        /// <param name="isRelay">Si es true, NO sobrescribe la configuración del transporte (necesario para Relay)</param>
        public static bool StartHost(ushort port = 7777, bool isRelay = false)
        {
            if (NetworkManager.Singleton == null) return false;

            if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.Shutdown();
            }

            if (!isRelay)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
                }
            }

            return NetworkManager.Singleton.StartHost();
        }

        public static bool StartClient(string ip, ushort port = 7777)
        {
            if (NetworkManager.Singleton == null) return false;

            // Asegurar un estado limpio antes de iniciar
            if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.Shutdown();
            }

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
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
