using UnityEngine;

namespace Runtime.Scripts.Optional
{
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

        public static implicit operator Optional<T>(T value)
        {
            return new Optional<T> { use = true, value = value };
        }

        public static implicit operator Optional<float>(Optional<int> optInt)
        {
            return new Optional<float> { use = optInt.use, value = optInt.value };
        }
    }
}
