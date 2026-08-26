using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Unity.Burst;

namespace NGO.Networking
{
    public static class NetworkingService
    {
        // Esto se ejecuta antes que CUALQUIER otra cosa en el juego
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void DisableBurstRoot()
        {
            BurstCompiler.Options.EnableBurstCompilation = false;
            Debug.Log("<color=red><b>[NGO] BURST DESACTIVADO DE RAÍZ.</b></color>");
        }

        public static bool StartHost(ushort port = 7777, bool isRelay = false)
        {
            if (NetworkManager.Singleton == null) return false;

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

        public static bool StartClient(string ip = "127.0.0.1", ushort port = 7777, bool isRelay = false)
        {
            if (NetworkManager.Singleton == null) return false;

            if (!isRelay)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    transport.SetConnectionData(ip, port);
                }
            }

            return NetworkManager.Singleton.StartClient();
        }

        public static void Shutdown()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();
        }
    }
}
