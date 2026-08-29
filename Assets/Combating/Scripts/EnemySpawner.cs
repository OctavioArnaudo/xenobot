using UnityEngine;
using Unity.Netcode;

// Spawnea enemigos cada cierto tiempo en una posicin aleatoria dentro de un radio.
// Funciona en red (autoridad del servidor) y en offline.
public class EnemySpawner : NetworkBehaviour
{
    [Header("Prefabs")]
    public GameObject meleeEnemyPrefab;
    public GameObject rangedEnemyPrefab;

    [Header("Spawn")]
    public float spawnInterval = 300f; // 5 minutos por defecto
    public float spawnRadius = 15f;

    float m_Timer;

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;
    private bool CanExecuteLogic => !IsNetworkActive || IsServer;

    void Update()
    {
        // Solo ejecuta la lgica si es offline o si es el servidor en online
        if (!CanExecuteLogic) return;

        m_Timer += Time.deltaTime;
        if (m_Timer >= spawnInterval)
        {
            m_Timer = 0f;
            SpawnRandomEnemy();
        }
    }

    void SpawnRandomEnemy()
    {
        var prefab = (Random.value < 0.5f) ? meleeEnemyPrefab : rangedEnemyPrefab;
        if (prefab == null) return;

        Vector2 offset = Random.insideUnitCircle * spawnRadius;
        Vector3 pos = transform.position + new Vector3(offset.x, 0f, offset.y);

        var go = Instantiate(prefab, pos, Quaternion.identity);

        // Spawnear en la red si es necesario
        if (IsNetworkActive)
        {
            if (go.TryGetComponent<NetworkObject>(out var netObj))
            {
                netObj.Spawn();
            }
            else
            {
                Debug.LogWarning($"El enemigo instanciado no tiene NetworkObject, no se puede spawnear en red.");
            }
        }
    }
}
