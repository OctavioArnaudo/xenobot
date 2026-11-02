using UnityEngine;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Runtime.Scripts.Optional;

namespace Runtime.Scripts.Effective
{
    public static class Effective
    {
        public static T Get<T>(
            this Optional<T> optional,
            T runtimeValue,
            T defaultValue,
            Optional<T> balance = default,
            [CallerMemberName] string propertyName = ""
        ) where T : System.IEquatable<T>
        {
            try
            {
                if (Application.isPlaying && !EqualityComparer<T>.Default.Equals(runtimeValue, defaultValue))
                {
                    return runtimeValue;
                }
                if (optional.use)
                {
                    return optional.value;
                }
                if (balance.use)
                {
                    return balance.value;
                }
                return defaultValue;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Fallback] {propertyName}: {ex.Message}");
                return defaultValue;
            }
        }
    }
}
