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
        )
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

        public static float Get(
            this Optional<float> optional,
            float runtimeValue,
            float defaultValue,
            Optional<int> balanceInt,
            [CallerMemberName] string propertyName = ""
        )
        {
            Optional<float> balanceFloat = new Optional<float> { use = balanceInt.use, value = balanceInt.value };
            return optional.Get(runtimeValue, defaultValue, balanceFloat, propertyName);
        }

        public static int Get(
            this Optional<int> optional,
            int runtimeValue,
            int defaultValue,
            Optional<float> balanceFloat,
            [CallerMemberName] string propertyName = ""
        )
        {
            Optional<int> balanceInt = new Optional<int> { use = balanceFloat.use, value = Mathf.RoundToInt(balanceFloat.value) };
            return optional.Get(runtimeValue, defaultValue, balanceInt, propertyName);
        }

        public static float Get(
            this Optional<float> optional,
            float runtimeValue,
            float defaultValue,
            Optional<string> balanceStr,
            [CallerMemberName] string propertyName = ""
        )
        {
            float parsedVal = 0f;
            bool valid = balanceStr.use && float.TryParse(balanceStr.value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out parsedVal);
            Optional<float> balanceFloat = new Optional<float> { use = valid, value = parsedVal };
            return optional.Get(runtimeValue, defaultValue, balanceFloat, propertyName);
        }

        public static int Get(
            this Optional<int> optional,
            int runtimeValue,
            int defaultValue,
            Optional<string> balanceStr,
            [CallerMemberName] string propertyName = ""
        )
        {
            int parsedVal = 0;
            bool valid = balanceStr.use && int.TryParse(balanceStr.value, out parsedVal);
            Optional<int> balanceInt = new Optional<int> { use = valid, value = parsedVal };
            return optional.Get(runtimeValue, defaultValue, balanceInt, propertyName);
        }

        public static string Get(
            this Optional<string> optional,
            string runtimeValue,
            string defaultValue,
            Optional<int> balanceInt,
            [CallerMemberName] string propertyName = ""
        )
        {
            Optional<string> balanceStr = new Optional<string> { use = balanceInt.use, value = balanceInt.value.ToString() };
            return optional.Get(runtimeValue, defaultValue, balanceStr, propertyName);
        }

        public static string Get(
            this Optional<string> optional,
            string runtimeValue,
            string defaultValue,
            Optional<float> balanceFloat,
            [CallerMemberName] string propertyName = ""
        )
        {
            Optional<string> balanceStr = new Optional<string> { use = balanceFloat.use, value = balanceFloat.value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            return optional.Get(runtimeValue, defaultValue, balanceStr, propertyName);
        }
    }
}
