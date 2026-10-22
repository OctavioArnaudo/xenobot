using UnityEngine;

namespace NGO.Networking
{
    /// <summary>
    /// Asegura que el objeto sea una instancia única y persista entre escenas.
    /// Útil para el NetworkManager y managers de lógica global.
    /// </summary>
    public class Singleton : MonoBehaviour
    {
        private static Singleton s_Instance;

        private void Awake()
        {
            // Evitar duplicados si volvemos a la escena inicial
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;

            // Asegurar que sea raíz y persista
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }
    }
}
