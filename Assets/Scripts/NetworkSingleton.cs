using Unity.Netcode;
using UnityEngine;

    /// <summary>
    /// Un Singleton para NetworkBehaviours que solo gestiona duplicados si el cliente local es el dueño.
    /// Esto evita que los prefabs de otros jugadores (proxies) sean destruidos accidentalmente
    /// cuando intentan registrarse como la instancia local.
    /// </summary>
    /// <typeparam name="T">El tipo del componente que hereda de NetworkBehaviour</typeparam>
    public class NetworkSingleton<T> : NetworkBehaviour where T : NetworkBehaviour
    {
        private static T s_Instance;

        public static T Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    // Fallback: Buscar en la escena un objeto de este tipo que sea propiedad del cliente local
                    T[] foundObjects = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
                    foreach (T obj in foundObjects)
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

            // Si NO somos los dueños de este objeto, no es "nuestra" instancia singleton.
            // Es la representación (proxy) de otro jugador en nuestro mundo.
            if (!IsOwner) return;

            if (s_Instance != null && s_Instance != this)
            {
                // Solo destruimos el objeto si somos dueños de ambos.
                // Esto sucede típicamente si el objeto persiste entre escenas pero se spawnea uno nuevo erróneamente.
                if (s_Instance.IsOwner)
                {
                    Debug.LogWarning($"[NetworkSingleton] Se detectó un duplicado de {typeof(T).Name} del cual eres dueño. Destruyendo el nuevo para mantener la unicidad local.");

                    gameObject.SetActive(false);
                    Destroy(gameObject);
                    return;
                }
            }

            // Establecer como la instancia local de referencia
            s_Instance = this as T;
            Debug.Log($"[NetworkSingleton] {typeof(T).Name} registrado como instancia local para el Owner.");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            // Limpiar la referencia estática solo si el objeto que desaparece es nuestra instancia
            if (IsOwner && s_Instance == this)
            {
                s_Instance = null;
            }
        }
    }