using UnityEngine;

/// <summary>
/// Generic serializable struct for Optional Inspector overrides.
/// </summary>
[System.Serializable]
public struct Optional<T>
{
    [Tooltip("Activar para sobrescribir manualmente el valor por defecto interno")]
    public bool useOverride;
    [Tooltip("Valor manual asignado si useOverride está activado (puede ser 0, false, Vector3.zero, etc.)")]
    public T value;

    public T GetValue(T defaultValue)
    {
        return useOverride ? value : defaultValue;
    }
}