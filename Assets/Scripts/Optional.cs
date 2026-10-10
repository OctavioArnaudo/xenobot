using UnityEngine;

/// <summary>
/// Generic serializable struct for Optional Inspector overrides.
/// </summary>
[System.Serializable]
public struct Optional<T>
{
    [Tooltip("Activar para sobrescribir manualmente el valor")]
    public bool use;
    [Tooltip("Valor manual asignado si use está activado")]
    public T value;

    public readonly T GetValue(T defaultValue)
    {
        return use ? value : defaultValue;
    }
}