using System;
using System.Collections.Generic;
using UnityEngine;

namespace ISTPT.Core
{
    /// <summary>
    /// A simple Service Locator to decouple Data/Logic (Monitor) from Presentation (UI).
    /// </summary>
    public class ServiceLocator
    {
        private static ServiceLocator _instance;
        public static ServiceLocator Instance => _instance ??= new ServiceLocator();

        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        public void Register<T>(T service)
        {
            var type = typeof(T);
            if (!_services.ContainsKey(type))
            {
                _services.Add(type, service);
            }
            else
            {
                _services[type] = service;
            }
        }

        public T Get<T>()
        {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var service))
            {
                return (T)service;
            }
            
            Debug.LogError($"[ISTPT] Service of type {type} not registered.");
            return default;
        }

        public void Unregister<T>()
        {
             var type = typeof(T);
            if (_services.ContainsKey(type))
            {
                _services.Remove(type);
            }
        }
    }
}
