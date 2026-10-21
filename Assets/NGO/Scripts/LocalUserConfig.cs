using UnityEngine;

namespace NGO.Networking
{
    /// <summary>
    /// Almacena localmente las elecciones del usuario en el menú
    /// antes de que se spawnee su objeto de red usando PlayerPrefs.
    /// </summary>
    public static class LocalUserConfig
    {
        private const string KEY_USER_NAME = "LocalUserConfig_UserName";
        private const string KEY_USER_CUSTOM_ID = "LocalUserConfig_UserCustomID";
        private const string KEY_MAX_PLAYERS = "LocalUserConfig_MaxPlayers";
        private const string KEY_LAST_JOIN_CODE = "LocalUserConfig_LastJoinCode";

        public static string UserName
        {
            get => PlayerPrefs.GetString(KEY_USER_NAME, "Player");
            set { PlayerPrefs.SetString(KEY_USER_NAME, value); PlayerPrefs.Save(); }
        }

        public static int UserCustomID
        {
            get => PlayerPrefs.GetInt(KEY_USER_CUSTOM_ID, 0);
            set { PlayerPrefs.SetInt(KEY_USER_CUSTOM_ID, value); PlayerPrefs.Save(); }
        }

        public static int MaxPlayers
        {
            get => PlayerPrefs.GetInt(KEY_MAX_PLAYERS, 4);
            set { PlayerPrefs.SetInt(KEY_MAX_PLAYERS, value); PlayerPrefs.Save(); }
        }

        public static string LastJoinCode
        {
            get => PlayerPrefs.GetString(KEY_LAST_JOIN_CODE, "");
            set { PlayerPrefs.SetString(KEY_LAST_JOIN_CODE, value); PlayerPrefs.Save(); }
        }
    }
}
