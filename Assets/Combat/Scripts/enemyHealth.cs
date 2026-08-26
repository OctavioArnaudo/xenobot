using UnityEngine;
using Unity.Netcode;

public class enemyHealth : NetworkBehaviour
{
    public int maxHealth = 100;

    [Header("Muerte")]
    public ExpOrb expOrbPrefab; // Tu Prefab de la orbe (debe tener un NetworkObject)
    public int orbCount = 6;
    public float spreadRadius = 3f;

    // Red: Sincroniza la vida automticamente (Todos leen, solo Servidor escribe)
    private NetworkVariable<int> currentHealth = new NetworkVariable<int>(100);
    private int m_OfflineHealth;

    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    void Awake()
    {
        m_OfflineHealth = maxHealth;
    }

    public override void OnNetworkSpawn()
    {
        // Solo el servidor inicializa el valor de la NetworkVariable
        if (IsServer)
        {
            currentHealth.Value = maxHealth;
        }
    }

    /// <summary>
    /// Funcin pblica para aplicar dao, funciona en red (servidor) y local.
    /// </summary>
    public void TakeDamage(int damage)
    {
        if (IsNetworkActive)
        {
            if (!IsServer) return;
            if (currentHealth.Value <= 0) return;

            currentHealth.Value -= damage;
            Debug.Log($"{gameObject.name} recibi {damage} de dao (Red). Vida restante: {currentHealth.Value}");

            if (currentHealth.Value <= 0)
            {
                Die();
            }
        }
        else
        {
            if (m_OfflineHealth <= 0) return;

            m_OfflineHealth -= damage;
            Debug.Log($"{gameObject.name} recibi {damage} de dao (Offline). Vida restante: {m_OfflineHealth}");

            if (m_OfflineHealth <= 0)
            {
                Die();
            }
        }
    }

    private void Die()
    {
        // En red, solo el servidor ejecuta la muerte
        if (IsNetworkActive && !IsServer) return;

        Debug.Log(gameObject.name + " muri");

        // Genera las orbes
        SpawnExpOrbs();

        // Despawnea el objeto en toda la red o lo destruye localmente
        if (IsNetworkActive && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void SpawnExpOrbs()
    {
        if (expOrbPrefab == null) return;

        for (int i = 0; i < orbCount; i++)
        {
            // Dispersin horizontal tipo piata
            Vector2 offset = Random.insideUnitCircle * spreadRadius;
            Vector3 spawnPos = transform.position + new Vector3(offset.x, 0.6f, offset.y);

            // Instanciar el prefab
            ExpOrb orb = Instantiate(expOrbPrefab, spawnPos, Quaternion.identity);

            // Spawnear en la red si es necesario
            if (IsNetworkActive)
            {
                if (orb.TryGetComponent<NetworkObject>(out NetworkObject targetNetworkObject))
                {
                    targetNetworkObject.Spawn();
                }
                else
                {
                    Debug.LogError($"El prefab {expOrbPrefab.name} no tiene un NetworkObject asignado.");
                }
            }
        }
    }
}
