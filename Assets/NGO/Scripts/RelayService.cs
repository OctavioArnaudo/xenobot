using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace NGO.Networking
{
    /// <summary>
    /// Servicio modular para gestionar Unity Relay.
    /// Permite crear y unirse a salas mediante códigos sin necesidad de abrir puertos o usar IPs.
    /// </summary>
    public static class RelayManager
    {
        private static bool s_IsInitialized = false;

        /// <summary>
        /// Inicializa los servicios de Unity de forma asíncrona.
        /// </summary>
        public static async Task<bool> InitializeAsync()
        {
            if (s_IsInitialized) return true;

            try
            {
                await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
                s_IsInitialized = true;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RelayService] Error de inicialización: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Crea una asignación en Relay y devuelve el código de unión.
        /// </summary>
        public static async Task<string> CreateRelay(int maxConnections)
        {
            if (!await InitializeAsync()) return null;

            try
            {
                Allocation allocation = await Unity.Services.Relay.RelayService.Instance.CreateAllocationAsync(maxConnections);
                string joinCode = await Unity.Services.Relay.RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                if (NetworkManager.Singleton == null)
                {
                    Debug.LogError("[RelayManager] NetworkManager.Singleton es NULL. Asegúrate de que el objeto NetworkManager esté en la escena.");
                    return null;
                }

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    Debug.LogError("[RelayManager] UnityTransport no encontrado en el NetworkManager.");
                    return null;
                }

                transport.SetRelayServerData(
                    allocation.RelayServer.IpV4,
                    (ushort)allocation.RelayServer.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData
                );

                return joinCode;
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"[RelayService] Error al crear Relay: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Se une a una sesión de Relay existente usando un código.
        /// </summary>
        public static async Task<bool> JoinRelay(string joinCode)
        {
            if (!await InitializeAsync()) return false;

            try
            {
                JoinAllocation joinAllocation = await Unity.Services.Relay.RelayService.Instance.JoinAllocationAsync(joinCode);

                if (NetworkManager.Singleton == null)
                {
                    Debug.LogError("[RelayManager] NetworkManager.Singleton es NULL.");
                    return false;
                }

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    Debug.LogError("[RelayManager] UnityTransport no encontrado.");
                    return false;
                }

                transport.SetRelayServerData(
                    joinAllocation.RelayServer.IpV4,
                    (ushort)joinAllocation.RelayServer.Port,
                    joinAllocation.AllocationIdBytes,
                    joinAllocation.Key,
                    joinAllocation.ConnectionData,
                    joinAllocation.HostConnectionData
                );

                return true;
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"[RelayService] Error al unirse a Relay: {e.Message}");
                return false;
            }
        }
    }
}
