using UnityEngine;
using UnityEngine.SceneManagement;

namespace Runtime.Scripts.Gate
{
    /// <summary>
    /// Un solo menú a la vez. Cada menú pide paso con TryOpen("clave") antes de abrirse y lo libera con Close("clave").
    /// Mientras haya uno abierto, cualquier otro se ignora. Se limpia al cambiar de escena.
    /// </summary>
    public static class MenuGate
    {
        static string _owner;

        public static bool Busy => _owner != null;

        public static bool TryOpen(string key)
        {
            if (_owner != null && _owner != key) return false;
            _owner = key;
            return true;
        }

        public static void Close(string key)
        {
            if (_owner == key) _owner = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            _owner = null;
            SceneManager.sceneLoaded -= OnScene;
            SceneManager.sceneLoaded += OnScene;
        }

        static void OnScene(Scene s, LoadSceneMode m) => _owner = null;
    }
}