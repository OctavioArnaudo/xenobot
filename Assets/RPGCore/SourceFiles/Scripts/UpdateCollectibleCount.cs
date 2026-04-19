using UnityEngine;
using TMPro;

public class UpdateCollectibleCount : MonoBehaviour
{
    [Header("UI")]
    private TextMeshProUGUI collectibleText;

    [Header("Configuración")]
    [Tooltip("Debe coincidir con el nombre exacto del Layer 'Collectible' en Project Settings")]
    [SerializeField] private string collectibleLayerName = "Collectible";

    private int _layerMask;

    void Start()
    {
        collectibleText = GetComponent<TextMeshProUGUI>();
        if (collectibleText == null)
        {
            Debug.LogError("[UpdateCollectibleCount] Requiere un componente TextMeshProUGUI en el mismo GameObject.");
            return;
        }

        _layerMask = LayerMask.NameToLayer(collectibleLayerName);
        if (_layerMask == -1)
        {
            Debug.LogError($"[UpdateCollectibleCount] Layer '{collectibleLayerName}' no existe. Crearlo en Project Settings → Tags and Layers.");
            return;
        }

        UpdateCollectibleDisplay();
    }

    void Update()
    {
        UpdateCollectibleDisplay();
    }

    private void UpdateCollectibleDisplay()
    {
        // Busca todos los Pickup activos en la escena por layer, no por tipo
        Pickup[] activos = FindObjectsByType<Pickup>(FindObjectsSortMode.None);
        int total = 0;
        foreach (var p in activos)
        {
            if (p.gameObject.layer == _layerMask)
                total++;
        }
        collectibleText.text = $"Collectibles restantes: {total}";
    }
}