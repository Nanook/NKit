using System;
using System.Collections.Generic;

namespace Nanook.NKit
{
    internal class Properties
    {
        private string[] _origOrderKeys;
        private Dictionary<string, object> _props;

        internal Properties(params string[] keys)
        {
            _origOrderKeys = keys;
            _props = new Dictionary<string, object>();
            for (int i = 0; i < keys.Length; i++)
                _props.Add(keys[i], null);
        }

        private Properties(Dictionary<string, object> properties, string[] keys)
        {
            _origOrderKeys = keys;
            _props = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kv in properties)
                _props.Add(kv.Key, kv.Value);
        }

        public string[] Keys => _origOrderKeys;

        public T Get<T>(string key, T defaultVal)
        {
            if (_props.ContainsKey(key))
                return (T)(_props[key] ?? defaultVal);
            else
                return defaultVal;
        }
        public object this[string name]
        {
            get => _props.ContainsKey(name) ? _props[name] : null; internal set => _props[name] = value;
        }

        public TimeSpan GetTimeSpan(string key) => (TimeSpan)_props[key];

        public Properties Clone() => new Properties(_props, _origOrderKeys);
    }
}