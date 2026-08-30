using UnityEngine;
using UnityEngine.SceneManagement;

namespace NGO.Networking
{
    /// <summary>
    /// Manager principal para la navegación de los paneles de red en el Menú.
    /// </summary>
    public class NetworkMenuManager : MonoBehaviour
    {
        [Header("Paneles de Menú")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject hostPanel;
        [SerializeField] private GameObject clientPanel;

        private void Start()
        {
            // Forzar visibilidad del mouse en los paneles de red
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            Application.runInBackground = true;
            Debug.Log("[NetworkMenu] Manager inicializado correctamente.");
            ShowMainMenu();
        }

        public void ShowMainMenu()
        {
            SetAllPanelsInactive();
            if (mainPanel != null) mainPanel.SetActive(true);
        }

        public void ShowHostMenu()
        {
            SetAllPanelsInactive();
            if (hostPanel != null) hostPanel.SetActive(true);
        }

        public void ShowClientMenu()
        {
            SetAllPanelsInactive();
            if (clientPanel != null) clientPanel.SetActive(true);
        }

        private void SetAllPanelsInactive()
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            if (hostPanel != null) hostPanel.SetActive(false);
            if (clientPanel != null) clientPanel.SetActive(false);
        }

        public void GoBack()
        {
            SceneManager.LoadScene("MainMenuScene");
        }
    }
}
