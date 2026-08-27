using UnityEngine;
using Unity.Netcode;
using Xenobot.ModularCombat;

public class enemyHealth : NetworkBehaviour
{
    public int maxHealth = 100;

    [Header("Muerte")]
    public ExpOrb expOrbPrefab; // Tu Prefab de la orbe (debe tener un NetworkObject)
    public int orbCount = 6;
    public float spreadRadius = 3f;

    // Red: Sincroniza la vida automáticamente (Todos leen, solo Servidor escribe)
    private NetworkVariable<int> currentHealth = new NetworkVariable<int>(100);

    private CombatDamageReceiver m_DamageReceiver;
    private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    void Awake()
    {
        m_DamageReceiver = GetComponent<CombatDamageReceiver>();
        if (m_DamageReceiver == null)
            m_DamageReceiver = gameObject.AddComponent<CombatDamageReceiver>();

        m_DamageReceiver.MaxHealth = maxHealth;
        m_DamageReceiver.DestroyOnDeath = false; // Manejamos la muerte nosotros
        m_DamageReceiver.OnDied.AddListener(OnDied);
        m_DamageReceiver.OnDamaged.AddListener(OnDamaged);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            currentHealth.Value = maxHealth;
            m_DamageReceiver.Initialize(maxHealth);
        }

        currentHealth.OnValueChanged += (oldVal, newVal) => {
            if (!IsServer) m_DamageReceiver.SyncFrom(newVal);
        };
    }

    void OnDamaged(float damage)
    {
        if (IsServer)
        {
            currentHealth.Value = Mathf.RoundToInt(m_DamageReceiver.CurrentHealth);
        }
    }

    void OnDied()
    {
        Die();
    }

    /// <summary>
    /// Función pública para aplicar daño, redirige al sistema modular.
    /// </summary>
    public void TakeDamage(int damage)
    {
        m_DamageReceiver.TakeDamage(damage, null);
    }

    private void Die()
    {
        // En red, solo el servidor ejecuta la muerte
        if (IsNetworkActive && !IsServer) return;

        Debug.Log(gameObject.name + " murió");

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
