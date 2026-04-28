using Unity.Netcode;
using UnityEngine;
using System;

public class NetworkHealth : NetworkBehaviour
{
    [Header("Settings")]
    public int MaxHealth = 100;

    // Sincronización automática de la vida
    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(100);

    public event Action<int, int> OnHealthChanged; // (old, new)
    public event Action OnDeath;

    public override void OnNetworkSpawn()
    {
        CurrentHealth.OnValueChanged += HandleHealthChanged;
        if (IsServer)
        {
            CurrentHealth.Value = MaxHealth;
        }
    }

    public override void OnNetworkDespawn()
    {
        CurrentHealth.OnValueChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(int oldVal, int newVal)
    {
        OnHealthChanged?.Invoke(oldVal, newVal);
        if (newVal <= 0 && oldVal > 0)
        {
            OnDeath?.Invoke();
        }
    }

    // Solo el servidor debería llamar a esto
    public void ModifyHealth(int amount)
    {
        if (!IsServer) return;

        int nextHealth = Mathf.Clamp(CurrentHealth.Value + amount, 0, MaxHealth);
        CurrentHealth.Value = nextHealth;
    }
}
