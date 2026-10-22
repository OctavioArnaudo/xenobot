using UnityEngine;

namespace NGO.Networking
{
    /// <summary>
    /// Base para Singletons genéricos basados en MonoBehaviour (Managers de escena, sistemas, UI).
    /// Utiliza CSingleton como fuente de verdad central.
    /// </summary>
    public abstract class CSingletonMB<T> : MonoBehaviour where T : CSingletonMB<T>
    {
        public static T Instance => CSingleton<T>.Instance;
        public static bool HasInstance => CSingleton<T>.HasInstance;

        [Header("Singleton MB Settings")]
        [SerializeField] protected bool persistAcrossScenes = true;
        [SerializeField] protected bool destroyDuplicates = true;

        protected virtual void Awake()
        {
            T current = CSingleton<T>.Instance;

            if (current != null && current != this)
            {
                if (destroyDuplicates)
                {
                    Debug.LogWarning($"[CSingletonMB] Instancia duplicada de {typeof(T).Name} encontrada en {gameObject.name}. Destruyendo.");
                    Destroy(gameObject);
                    return;
                }
            }

            CSingleton<T>.Instance = this as T;

            if (persistAcrossScenes)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
        }

        protected virtual void OnDestroy()
        {
            CSingleton<T>.UnregisterInstance(this as T);
        }
    }
}
