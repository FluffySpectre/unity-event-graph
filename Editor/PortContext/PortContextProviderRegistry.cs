using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FluffySpectre.UnityEventGraph.PortContext
{
    public static class PortContextProviderRegistry
    {
        private static List<IPortContextProvider> _providers = new();

        static PortContextProviderRegistry()
        {
            FindAndRegisterProviders();
        }

        public static void RegisterProvider(IPortContextProvider provider)
        {
            if (!_providers.Contains(provider))
            {
                _providers.Add(provider);
            }
        }

        public static IPortContextProvider GetProviderFor(Component component)
        {
            return _providers.FirstOrDefault(p => p.CanProvideFor(component));
        }

        private static void FindAndRegisterProviders()
        {
            var types = System.AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p => typeof(IPortContextProvider).IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract);

            foreach (var type in types)
            {
                var provider = System.Activator.CreateInstance(type) as IPortContextProvider;
                RegisterProvider(provider);
            }
        }
    }
}
