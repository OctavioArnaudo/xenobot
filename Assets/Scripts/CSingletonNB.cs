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

        // FIX #1: Awake ya NO destruye nada.
        // El problema original era que s_Instance es static y compartido entre todas
        // las instancias del tipo. Cuando un segundo jugador spawneaba su prefab,
        // el Awake de cualquier instancia ya existente encontraba s_Instance != null
        // y destruía el objeto recién creado ANTES de que OnNetworkSpawn confirmara
        // el ownership real. Ahora Awake solo hace setup seguro.
        protected virtual void Awake() { }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Caso 1: Ya existe una instancia propia (viene de escena anterior persistente)
            // y este objeto nuevo también nos pertenece (el placeholder de la escena destino).
            // Solo en este caso destruimos: ambos son nuestros y el viejo tiene prioridad.
            if (s_Instance != null && s_Instance != this && s_Instance.IsOwner && IsOwner)
            {
                Debug.Log($"[NetworkSingleton] {typeof(T).Name} placeholder detectado en escena destino. Destruyendo duplicado local.");
                Destroy(gameObject);
                return;
            }

            // Caso 2: La instancia registrada no es nuestra (proxy remoto guardado por error)
            // pero este objeto sí lo es → tomamos el Singleton local.
            if (s_Instance != null && s_Instance != this && IsOwner && !s_Instance.IsOwner)
            {
                s_Instance = this as T;
            }
            // Caso 3: No había instancia todavía, o este objeto es el primero.
            else if (s_Instance == null || IsOwner)
            {
                s_Instance = this as T;
            }

            // Solo el dueño local persiste entre escenas.
            if (IsOwner)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
                Debug.Log($"[NetworkSingleton] {typeof(T).Name} marcado como persistente (DontDestroyOnLoad).");
            }
        }

        public override void OnNetworkDespawn()
        {
            if (s_Instance == this)
                s_Instance = null;

            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;

            base.OnDestroy();
        }
    }
}
