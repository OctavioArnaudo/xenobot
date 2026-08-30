using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Unity.Burst;

namespace NGO.Networking
{
    public static class NetworkingService
    {
        private static bool s_IsStarting = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void DisableBurstRoot()
        {
            BurstCompiler.Options.EnableBurstCompilation = false;
        }

        public static bool StartHost(ushort port = 7777, bool isRelay = false)
        {
            if (NetworkManager.Singleton == null || s_IsStarting) return false;

            // Si ya está activo (como Host, Server o Client), no hacemos nada
            if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
            {
                Debug.Log("[NetworkingService] Network already active. Skipping StartHost call.");
                return true;
            }

            s_IsStarting = true;
            try
            {
                if (!isRelay)
                {
                    var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                    if (transport != null)
                    {
                        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
                    }
                }

                bool success = NetworkManager.Singleton.StartHost();
                s_IsStarting = false;
                return success;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[NetworkingService] Exception during StartHost: {e.Message}");
                s_IsStarting = false;
                return false;
            }
        }

        public static bool StartClient(string ip = "127.0.0.1", ushort port = 7777, bool isRelay = false)
        {
            if (NetworkManager.Singleton == null || s_IsStarting) return false;

            if (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
            {
                Debug.Log("[NetworkingService] Network already active. Skipping StartClient call.");
                return true;
            }

            s_IsStarting = true;
            try
            {
                if (!isRelay)
                {
                    var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                    if (transport != null)
                    {
                        transport.SetConnectionData(ip, port);
                    }
                }

                bool success = NetworkManager.Singleton.StartClient();
                s_IsStarting = false;
                return success;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[NetworkingService] Exception during StartClient: {e.Message}");
                s_IsStarting = false;
                return false;
            }
        }

        public static void Shutdown()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();
        }
    }
}
