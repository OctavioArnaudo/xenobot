namespace Crafting.Scripts
{
    /// <summary>
    /// Interfaz base para todos los módulos que forman parte del ecosistema del Jugador.
    /// Permite una vinculación (Binding) explícita con el Hub central.
    /// </summary>
    public interface IPlayerModule
    {
        /// <summary>
        /// Vincula el módulo con el PlayerController central.
        /// Se llama inmediatamente después de la instanciación o en el Awake/OnNetworkSpawn.
        /// </summary>
        void Bind(PlayerController hub);

        /// <summary>
        /// Se llama cuando el Hub detecta un cambio importante en la jerarquía
        /// (ej: cambio de traje, cambio de animator).
        /// </summary>
        void OnRefreshModule();
    }
}
