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

        protected virtual void Awake()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                CSingleton<T>.Instance = this as T;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            T current = CSingleton<T>.Instance;
            if (current != null && current != this && (current as NetworkBehaviour)?.IsOwner == true && IsOwner)
            {
                Destroy(gameObject);
                return;
            }
            if (current != null && current != this && IsOwner && (current as NetworkBehaviour)?.IsOwner != true)
            {
                CSingleton<T>.Instance = this as T;
            }
            else if (current == null || IsOwner)
            {
                CSingleton<T>.Instance = this as T;
            }
            if (IsOwner)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
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
