using UnityEngine;
using UnityEngine.Events;

namespace Combating.Scripts
{
    public class DamageController : MonoBehaviour
    {
        public float MaxHealth = 100f;
        public bool DestroyOnDeath = true;
        public float DestroyDelay = 0f;
        public GameObject DeathVfxPrefab;
        public Transform DeathVfxSpawnPoint;
        public float DeathVfxLifetime = 4f;

        public UnityEvent<float> OnDamaged;
        public UnityEvent OnDied;

        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        void Awake()
        {
            CurrentHealth = MaxHealth;
        }

        public void TakeDamage(float damage, GameObject source)
        {
            if (IsDead || damage <= 0f)
                return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
            OnDamaged?.Invoke(damage);

            if (CurrentHealth <= 0f)
                Die();
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        }

        void Die()
        {
            IsDead = true;
            OnDied?.Invoke();

            if (DeathVfxPrefab != null)
            {
                Vector3 position = DeathVfxSpawnPoint != null ? DeathVfxSpawnPoint.position : transform.position;
                GameObject deathVfx = Instantiate(DeathVfxPrefab, position, Quaternion.identity);
                if (DeathVfxLifetime > 0f)
                    Destroy(deathVfx, DeathVfxLifetime);
            }

            if (DestroyOnDeath)
                Destroy(gameObject, DestroyDelay);
        }
    }
}
