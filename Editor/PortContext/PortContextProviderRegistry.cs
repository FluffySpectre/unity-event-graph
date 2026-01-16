using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FluffySpectre.UnityEventGraph.PortContext
{
    public static class PortContextProviderRegistry
    {
        private static List<IPortContextProvider> _providers = new();
        private static readonly Dictionary<Type, IPortContextProvider> _providerCache = new();
        private static bool _initialized = false;

        static PortContextProviderRegistry()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;
            
            FindAndRegisterProviders();
            _initialized = true;
        }

        public static void RegisterProvider(IPortContextProvider provider)
        {
            if (!_providers.Contains(provider))
            {
                _providers.Add(provider);
                _providerCache.Clear(); // Invalidate cache when providers change
            }
        }

        public static IPortContextProvider GetProviderFor(Component component)
        {
            if (component == null) return null;
            
            var componentType = component.GetType();
            
            // Check cache first
            if (_providerCache.TryGetValue(componentType, out var cachedProvider))
            {
                return cachedProvider;
            }
            
            // Find matching provider
            var provider = _providers.FirstOrDefault(p => p.CanProvideFor(component));
            
            // Cache the result (even if null)
            _providerCache[componentType] = provider;
            
            return provider;
        }

        private static void FindAndRegisterProviders()
        {
            try
            {
                var types = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(s => 
                    {
                        try { return s.GetTypes(); }
                        catch { return Array.Empty<Type>(); }
                    })
                    .Where(p => typeof(IPortContextProvider).IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract);

                foreach (var type in types)
                {
                    try
                    {
                        var provider = Activator.CreateInstance(type) as IPortContextProvider;
                        if (provider != null)
                        {
                            RegisterProvider(provider);
                        }
                    }
                    catch
                    {
                        // Ignore instantiation failures
                    }
                }
            }
            catch
            {
                // Ignore assembly enumeration failures
            }
        }
    }
}
