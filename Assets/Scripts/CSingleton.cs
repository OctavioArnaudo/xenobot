using UnityEngine;

namespace Runtime.Scripts.Singleton
{
    /// <summary>
    /// CSingleton.cs - Fuente de verdad central para la lógica base del Singleton.
    /// Administra de forma centralizada y segura el estado, la instancia, el bloqueo por hilos y la validación de cualquier tipo T.
    /// </summary>
    public static class CSingleton<T> where T : class
    {
        private static T s_Instance;
        private static readonly object s_Lock = new object();
        private static bool s_IsDestroyed = false;

        /// <summary>
        /// Obtiene o establece la instancia central del Singleton.
        /// </summary>
        public static T Instance
        {
            get
            {
                lock (s_Lock)
                {
                    if (s_IsDestroyed)
                    {
                        return null;
                    }
                    return s_Instance;
                }
            }
            set
            {
                lock (s_Lock)
                {
                    s_Instance = value;
                    s_IsDestroyed = false;
                }
            }
        }

        /// <summary>
        /// Indica si existe una instancia activa y válida.
        /// </summary>
        public static bool HasInstance => s_Instance != null && !s_IsDestroyed;

        /// <summary>
        /// Limpia y desregistra la instancia actual de forma segura.
        /// </summary>
        public static void UnregisterInstance(T currentInstanceRef)
        {
            lock (s_Lock)
            {
                if (s_Instance == currentInstanceRef)
                {
                    s_Instance = null;
                    s_IsDestroyed = true;
                }
            }
        }
    }
}
