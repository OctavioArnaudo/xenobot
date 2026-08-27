using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Unity.Burst;

namespace NGO.Networking
{
    public static class NetworkingService
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void DisableBurstRoot()
        {
            BurstCompiler.Options.EnableBurstCompilation = false;
        }

        public static bool StartHost(ushort port = 7777, bool isRelay = false)
        {
            if (NetworkManager.Singleton == null) return false;

            // Configuramos la aprobación para que el Host también pase por el filtro
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;

            if (!isRelay)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
                }
            }

            Debug.Log("[NetworkingService] Iniciando Host...");
            bool success = NetworkManager.Singleton.StartHost();

            // Si el inicio fue exitoso, nos aseguramos de que el PlayerObject del Host sea persistente
            // tal como lo hace el Inspector de Netcode.
            if (success)
            {
                // NGO suele tardar un frame en asignar el PlayerObject,
                // pero si ya existe, lo blindamos.
                var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
                if (localPlayer != null)
                {
                    localPlayer.transform.SetParent(null);
                    Object.DontDestroyOnLoad(localPlayer.gameObject);
                }
            }

            return success;
        }

        public static bool StartClient(string ip = "127.0.0.1", ushort port = 7777, bool isRelay = false)
        {
            if (NetworkManager.Singleton == null) return false;

            // El cliente debe coincidir en la configuración de aprobación
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;

            if (!isRelay)
            {
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    transport.SetConnectionData(ip, port);
                }
            }

            Debug.Log($"[NetworkingService] Iniciando Cliente hacia {ip}:{port}...");
            return NetworkManager.Singleton.StartClient();
        }

        private static void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            // Aprobamos la conexión
            response.Approved = true;

            // FORZAMOS la creación del PlayerObject.
            // La posición inicial ya no se define aquí, sino en el PlayerController según la escena.
            response.CreatePlayerObject = true;

            // Usamos el prefab asignado por defecto en el NetworkManager
            response.PlayerPrefabHash = null;

            Debug.Log($"[NetworkingService] Solicitada creación de PlayerObject para cliente {request.ClientNetworkId}.");
        }

        public static void Shutdown()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();
        }
    }
}
