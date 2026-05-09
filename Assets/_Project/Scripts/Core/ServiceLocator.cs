using System;
using System.Collections.Generic;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// Minimal service locator. Replaced with a proper DI container in later phases if needed.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, IService> _services = new();

        public static void Register<T>(T service) where T : IService
        {
            var type = typeof(T);
            if (_services.ContainsKey(type))
            {
                Debug.LogWarning($"[ServiceLocator] Overwriting existing service of type {type.Name}");
            }
            _services[type] = service;
        }

        public static T Get<T>() where T : class, IService
        {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var service))
                return service as T;

            throw new InvalidOperationException($"[ServiceLocator] Service not found: {type.Name}");
        }

        public static bool TryGet<T>(out T service) where T : class, IService
        {
            if (_services.TryGetValue(typeof(T), out var raw))
            {
                service = raw as T;
                return service != null;
            }
            service = null;
            return false;
        }

        public static void Clear() => _services.Clear();
    }
}
