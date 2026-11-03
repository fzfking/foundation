using System;
using UnityEngine;

namespace Foundation.UI.UGui
{
    [Serializable]
    public sealed class LocalizedTextArgument
    {
        [SerializeField] private string key;

        [SerializeField] private LocalizedTextArgumentType type;

        [SerializeField] private string stringValue;

        [SerializeField] private int integerValue;

        [SerializeField] private double decimalValue;

        [SerializeField] private bool booleanValue;

        public string Key => key;

        // Boxed once: the value is read on every text refresh and the authored fields do not change at runtime.
        [NonSerialized] private object _value;

        public object Value => _value ??= type switch
        {
            LocalizedTextArgumentType.Integer => integerValue,
            LocalizedTextArgumentType.Decimal => decimalValue,
            LocalizedTextArgumentType.Boolean => booleanValue,
            _ => stringValue,
        };
    }

    public enum LocalizedTextArgumentType
    {
        String,
        Integer,
        Decimal,
        Boolean,
    }
}
