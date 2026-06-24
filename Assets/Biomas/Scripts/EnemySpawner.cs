using UnityEngine;
using Unity.Netcode;

// Spawnea enemigos cada 5 minutos en una posición aleatoria dentro de un radio.
// Solo corre en el servidor (igual criterio que LobbyMenuManager: lógica de tiempo server-side).
public class EnemySpawner : NetworkBehaviour
{
    [Header("Prefabs")]
    public GameObject meleeEnemyPrefab;
    public GameObject rangedEnemyPrefab;

    [Header("Spawn")]
    public float spawnInterval = 300f; // 5 minutos
    public float spawnRadius = 15f;

    float m_Timer;

    void Update()
    {
        if (!IsServer) return;

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
        go.GetComponent<NetworkObject>().Spawn();
    }
}