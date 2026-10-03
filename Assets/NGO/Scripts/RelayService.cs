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
    public static class RelayManager
    {
        private static bool s_IsInitialized = false;

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
                Debug.LogError($"[RelayService] Error: {e.Message}");
                return false;
            }
        }

        public static async Task<string> CreateRelay(int maxConnections)
        {
            if (!await InitializeAsync()) return null;
            try
            {
                Allocation allocation = await Unity.Services.Relay.RelayService.Instance.CreateAllocationAsync(maxConnections);
                string joinCode = await Unity.Services.Relay.RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.SetRelayServerData(
                    allocation.RelayServer.IpV4,
                    (ushort)allocation.RelayServer.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData
                );

                return joinCode;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RelayService] Error al crear: {e.Message}");
                return null;
            }
        }

        public static async Task<bool> JoinRelay(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                Debug.LogWarning("[RelayService] El código de sala está vacío o es nulo.");
                return false;
            }

            // Normalizar el código eliminando espacios laterales y convirtiendo a mayúsculas
            joinCode = joinCode.Trim().ToUpper();

            if (!await InitializeAsync()) return false;
            try
            {
                JoinAllocation joinAllocation = await Unity.Services.Relay.RelayService.Instance.JoinAllocationAsync(joinCode);
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
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
            catch (RelayServiceException rse)
            {
                if (rse.Reason == RelayExceptionReason.JoinCodeNotFound || rse.Message.Contains("Not Found"))
                {
                    Debug.LogError($"[RelayService] El código de sala '{joinCode}' no existe, fue mal escrito o la sesión ha expirado.");
                }
                else
                {
                    Debug.LogError($"[RelayService] Error de Unity Relay ({rse.Reason}): {rse.Message}");
                }
                return false;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RelayService] Error al unirse: {e.Message}");
                return false;
            }
        }
    }
}
