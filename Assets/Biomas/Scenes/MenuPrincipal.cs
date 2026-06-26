using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class MenuPrincipal : MonoBehaviour
{
    [Header("Configuración de Escenas")]
    [SerializeField] private string nombreEscenaJuego = "BiomaScene";

    /// <summary>
    /// Se asignará al botón "Jugar". Inicia el juego en modo red (Host).
    /// </summary>
    public void Jugar()
    {
        if (NetworkManager.Singleton != null)
        {
            // Nos suscribimos al evento de Spawn para cambiar de escena en cuanto el network esté listo
            NetworkManager.Singleton.OnServerStarted += OnHostIniciado;

            // Iniciamos como Host (Jugador local + Servidor)
            NetworkManager.Singleton.StartHost();
        }
        else
        {
            Debug.LogError("No se encontró el NetworkManager en la escena del menú. Cargando en modo local/offline...");
            SceneManager.LoadScene(nombreEscenaJuego);
        }
    }

    private void OnHostIniciado()
    {
        // Dessuscribirse para evitar llamadas dobles
        NetworkManager.Singleton.OnServerStarted -= OnHostIniciado;

        // REGLA MULTIPLAYER: El cambio de escena se le ordena al SceneManager de Netcode
        NetworkManager.Singleton.SceneManager.LoadScene(
            nombreEscenaJuego,
            LoadSceneMode.Single
        );
    }

    /// <summary>
    /// Se asignará al botón "Salir".
    /// </summary>
    public void Salir()
    {
        Debug.Log("Saliendo del juego...");
        Application.Quit();
    }
}