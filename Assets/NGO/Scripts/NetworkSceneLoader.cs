using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NGO.Networking
{
    /// <summary>
    /// Script modular para manejar la carga de escenas sincronizada en red.
    /// </summary>
    public class NetworkSceneLoader : MonoBehaviour
    {
        public void LoadScene(string sceneName)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                Debug.Log($"[NetworkSceneLoader] Cargando escena: {sceneName}");
                NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            }
            else
            {
                Debug.LogWarning("[NetworkSceneLoader] Solo el servidor/host puede cargar escenas sincronizadas.");
            }
        }
    }
}
