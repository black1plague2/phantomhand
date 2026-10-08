using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Opus.Sdk
{
    /// <summary>
    /// Discovers every <see cref="IGameModule"/> implementation carrying a <see cref="GameModuleAttribute"/>
    /// across all loaded assemblies. Adding a game means adding a folder + attribute — zero edits here or
    /// in the shell (goal G1 in ARCHITECTURE.md §2).
    /// </summary>
    public static class GameRegistry
    {
        private static Dictionary<string, Type> _cache;

        /// <summary>Maps gameId -> concrete IGameModule Type. Cached after first scan; call <see cref="Rescan"/> to force a refresh (tests, editor reloads).</summary>
        public static IReadOnlyDictionary<string, Type> Games
        {
            get
            {
                if (_cache == null) Rescan();
                return _cache;
            }
        }

        public static void Rescan()
        {
            var found = new Dictionary<string, Type>();
            foreach (var assembly in SafeGetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

                foreach (var type in types)
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(IGameModule).IsAssignableFrom(type)) continue;
                    var attr = type.GetCustomAttribute<GameModuleAttribute>();
                    if (attr == null) continue;

                    if (found.TryGetValue(attr.GameId, out var existing) && existing != type)
                        throw new InvalidOperationException($"Duplicate GameModuleAttribute id '{attr.GameId}' on {existing.FullName} and {type.FullName}");

                    found[attr.GameId] = type;
                }
            }
            _cache = found;
        }

        public static IGameModule Create(string gameId)
        {
            if (!Games.TryGetValue(gameId, out var type))
                throw new KeyNotFoundException($"No IGameModule registered for id '{gameId}'");
            return (IGameModule)Activator.CreateInstance(type);
        }

        private static IEnumerable<Assembly> SafeGetAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies();
        }
    }
}
