using UnityEngine;
using Unity.Netcode;

public class enemyHealth : NetworkBehaviour
{
    public int maxHealth = 100;

    [Header("Muerte")]
    public ExpOrb expOrbPrefab;
    public int orbCount = 6;
    public float spreadRadius = 3f;

    NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (IsServer) currentHealth.Value = maxHealth;
    }

    [Rpc(SendTo.Server)]
    public void RequestDamageRpc(int damage)
    {
        if (currentHealth.Value <= 0) return;

        currentHealth.Value -= damage;
        Debug.Log(gameObject.name + " recibió " + damage + " de daño");

        if (currentHealth.Value <= 0)
            Die();
    }

    void Die()
    {
        Debug.Log(gameObject.name + " murió");
        SpawnExpOrbs();
        if (NetworkObject.IsSpawned) NetworkObject.Despawn();
        else Destroy(gameObject);
    }

    void SpawnExpOrbs()
    {
        if (expOrbPrefab == null) return;

        for (int i = 0; i < orbCount; i++)
        {
            // Dispersión horizontal tipo piñata, sin física extra
            Vector2 offset = Random.insideUnitCircle * spreadRadius;
            Vector3 spawnPos = transform.position + new Vector3(offset.x, 0.6f, offset.y);

            var orb = Instantiate(expOrbPrefab, spawnPos, Quaternion.identity);
            orb.GetComponent<NetworkObject>().Spawn();
        }
    }
}