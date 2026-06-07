using UnityEngine;

namespace NGO.Networking
{
    /// <summary>
    /// Almacena localmente las elecciones del usuario en el menú
    /// antes de que se spawnee su objeto de red.
    /// </summary>
    public static class LocalUserConfig
    {
        public static string UserName = "Player";
        public static Color UserColor = Color.white;
        public static int UserCustomID = 0;
        public static int MaxPlayers = 4;
    }
}
