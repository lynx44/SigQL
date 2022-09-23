using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SigQL.Utilities;

namespace SigQL
{
    public class RowValueCollection
    {
        public RowValueCollection()
        {
            this.Rows = new ConcurrentDictionary<RowKey, RowValues>();
        }

        public IDictionary<RowKey, RowValues> Rows { get; set; }

        public bool ContainsKey(RowKey key)
        {
            return Rows.ContainsKey(key);
        }
    }
    
    public class RowValues
    {
        public RowValues()
        {
            this.Relations = new ConcurrentDictionary<string, RowValueCollection>();
        }
        public IDictionary<string, object> Values { get; set; }
        public IDictionary<string, RowValueCollection> Relations { get; set; }
        public int RowNumber { get; set; }
    }

    public class RowKey
    {
        public RowKey(IEnumerable<KeyValue> keys)
        {
            Keys = keys;
        }
        public IEnumerable<KeyValue> Keys { get; set; }

        public override bool Equals(object obj)
        {
            if (this.Keys.Any())
            {
                var rowKey = obj as RowKey;
                return (rowKey != null && Enumerable.SequenceEqual(rowKey.Keys, this.Keys)) || base.Equals(obj);
            }

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            if (Keys.Any())
            {
                var hashCode = HashCodeUtility.Combine(Keys.Select(k => k.GetHashCode()).ToList());
                return hashCode;
            }
            
            return base.GetHashCode(); ;
        }
    }

    public class KeyValue
    {
        public KeyValue(string columnName, object value)
        {
            ColumnName = columnName;
            Value = value;
        }
        public string ColumnName { get; set; }
        public object Value { get; set; }
        public override bool Equals(object obj)
        {
            var keyValue = obj as KeyValue;

            return (keyValue != null && keyValue.ColumnName == ColumnName && (keyValue.Value?.Equals(Value)).GetValueOrDefault(false)) || base.Equals(obj);
        }

        public override int GetHashCode()
        {
            var hashCode = new Tuple<string, object>(this.ColumnName, this.Value).GetHashCode();
            return hashCode;
        }
    }
}