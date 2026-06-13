using UnityEngine;

/// <summary>
/// Sistema de progresión del personaje.
/// Attach al Player. Stats iniciales aleatorios, suben con EXP.
/// </summary>
public class CharacterStats : MonoBehaviour
{
    public static CharacterStats Instance { get; private set; }

    [Header("Rangos iniciales (random al Start)")]
    public Vector2 attackRange = new Vector2(5f, 15f);
    public Vector2 defenseRange = new Vector2(3f, 10f);

    [Header("Crecimiento base por nivel")]
    public float attackPerLevel = 2f;
    public float defensePerLevel = 1.5f;

    [Header("EXP para subir de nivel")]
    public float expToLevelUp = 100f;   // sube 20% por nivel

    // Valores actuales (readonly desde fuera)
    public float Attack { get; private set; }
    public float Defense { get; private set; }
    public int Level { get; private set; } = 1;
    public float Exp { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // Randomización inicial
        Attack = Random.Range(attackRange.x, attackRange.y);
        Defense = Random.Range(defenseRange.x, defenseRange.y);

        Debug.Log($"[Stats] Nivel {Level} | ATK {Attack:F1} | DEF {Defense:F1}");
    }

    /// <summary>Llamar al recoger un ítem EXP.</summary>
    public void AddExp(float amount)
    {
        Exp += amount;
        while (Exp >= expToLevelUp)
        {
            Exp -= expToLevelUp;
            LevelUp();
        }
    }

    void LevelUp()
    {
        Level++;
        Attack += attackPerLevel;
        Defense += defensePerLevel;
        expToLevelUp *= 1.2f; // cada nivel cuesta 20% más

        Debug.Log($"[Stats] ¡Nivel {Level}! | ATK {Attack:F1} | DEF {Defense:F1}");
    }
}