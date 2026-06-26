using UnityEngine;
using Unity.Netcode;

public class enemyHealth : NetworkBehaviour
{
    public int maxHealth = 100;

    [Header("Muerte")]
    public ExpOrb expOrbPrefab; // Tu Prefab de la orbe (debe tener un NetworkObject)
    public int orbCount = 6;
    public float spreadRadius = 3f;

    // Red: Sincroniza la vida automáticamente (Todos leen, solo Servidor escribe)
    private NetworkVariable<int> currentHealth = new NetworkVariable<int>(100);

    public override void OnNetworkSpawn()
    {
        // Solo el servidor inicializa el valor de la NetworkVariable
        if (IsServer)
        {
            currentHealth.Value = maxHealth;
        }
    }

    /// <summary>
    /// Función pública ejecutada estrictamente en el Servidor para aplicar daño.
    /// </summary>
    public void TakeDamage(int damage)
    {
        if (!IsServer) return;
        if (currentHealth.Value <= 0) return;

        currentHealth.Value -= damage;
        Debug.Log($"{gameObject.name} recibió {damage} de daño. Vida restante: {currentHealth.Value}");

        if (currentHealth.Value <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        if (!IsServer) return;

        Debug.Log(gameObject.name + " murió");

        // Genera las orbes en red antes de destruir el enemigo
        SpawnExpOrbs();

        // Despawnea el objeto en toda la red de forma limpia
        if (NetworkObject.IsSpawned)
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
            // Dispersión horizontal tipo piñata
            Vector2 offset = Random.insideUnitCircle * spreadRadius;
            Vector3 spawnPos = transform.position + new Vector3(offset.x, 0.6f, offset.y);

            // Instanciar el prefab en el servidor
            ExpOrb orb = Instantiate(expOrbPrefab, spawnPos, Quaternion.identity);

            // Spawnear en la red para que aparezca visualmente en todos los clientes
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