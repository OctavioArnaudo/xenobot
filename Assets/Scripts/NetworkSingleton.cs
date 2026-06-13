using Unity.Netcode;
using UnityEngine;

namespace NGO.Networking
{
    /// <summary>
    /// Base para Singletons de red que gestiona transiciones entre escenas.
    /// Si al cargar una escena nueva ya existe un objeto persistente del jugador,
    /// el objeto "placeholder" de la escena de destino se destruirá a sí mismo.
    /// </summary>
    public abstract class NetworkSingleton<T> : NetworkBehaviour where T : NetworkBehaviour
    {
        private static T s_Instance;

        public static T Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    T[] objects = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
                    foreach (var obj in objects)
                    {
                        if (obj.IsOwner)
                        {
                            s_Instance = obj;
                            break;
                        }
                    }
                }
                return s_Instance;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (s_Instance != null && s_Instance != this)
            {
                // Si ya existe una instancia de la que soy dueño (la que viene de la escena anterior)
                // y este nuevo objeto también es mío (el que estaba puesto en la escena de destino):
                if (IsOwner && s_Instance.IsOwner)
                {
                    Debug.Log($"[NetworkSingleton] Se detectó un {typeof(T).Name} en la escena de destino. Destruyendo duplicado local para mantener el personaje persistente.");

                    // Nos destruimos a nosotros mismos (el objeto de la escena de destino)
                    if (gameObject != null) Destroy(gameObject);
                    return;
                }

                // Si la instancia actual no es nuestra (proxy) pero este objeto nuevo sí lo es,
                // tomamos el control del Singleton local para el cliente.
                if (IsOwner && !s_Instance.IsOwner)
                {
                    s_Instance = this as T;
                }
            }
            else if (IsOwner || s_Instance == null)
            {
                s_Instance = this as T;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
            base.OnDestroy();
        }
    }
}
