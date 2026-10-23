using Unity.Netcode;
using UnityEngine;

namespace Runtime.Scripts.Singleton
{
    /// <summary>
    /// Base para Singletons de red (NetworkBehaviour) optimizada para Player Prefabs en NetworkManager.
    /// Utiliza CSingleton como fuente de verdad central.
    /// </summary>
    public abstract class CSingletonNB<T> : NetworkBehaviour where T : CSingletonNB<T>
    {
        public static T Instance => CSingleton<T>.Instance;
        public static bool HasInstance => CSingleton<T>.HasInstance;

        protected virtual void Awake() { }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            T current = CSingleton<T>.Instance;

            // Caso 1: Ya existe una instancia propia persistente y este objeto nuevo es un placeholder en la escena destino.
            if (current != null && current != this && (current as NetworkBehaviour)?.IsOwner == true && IsOwner)
            {
                Debug.Log($"[CSingletonNB] {typeof(T).Name} placeholder detectado en escena destino. Destruyendo duplicado local.");
                Destroy(gameObject);
                return;
            }

            // Caso 2: La instancia registrada no es nuestra pero este objeto sí lo es -> tomamos el Singleton local.
            if (current != null && current != this && IsOwner && (current as NetworkBehaviour)?.IsOwner != true)
            {
                CSingleton<T>.Instance = this as T;
            }
            // Caso 3: No había instancia todavía, o este objeto es el primero / nuestro.
            else if (current == null || IsOwner)
            {
                CSingleton<T>.Instance = this as T;
            }

            // Solo el dueño local persiste entre escenas.
            if (IsOwner)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
                Debug.Log($"[CSingletonNB] {typeof(T).Name} marcado como persistente (DontDestroyOnLoad).");
            }
        }

        public override void OnNetworkDespawn()
        {
            CSingleton<T>.UnregisterInstance(this as T);
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            CSingleton<T>.UnregisterInstance(this as T);
            base.OnDestroy();
        }
    }
}
