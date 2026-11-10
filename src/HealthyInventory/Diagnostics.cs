using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.HealthSystem;

namespace HealthyInventory
{
    /// <summary>
    /// Counts the handlers on the health controller's effect events, for the verbose log. Each
    /// health controller class names its backing fields differently (ObservedPlayerHealthController
    /// has _effectStartedEvent), so the field is found by delegate type and a name fragment and
    /// cached per class. Runs once per open, never per frame. If nothing matches it says so.
    /// </summary>
    internal static class Diagnostics
    {
        private static readonly Dictionary<Type, FieldInfo> Started = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> Removed = new Dictionary<Type, FieldInfo>();

        internal static string HandlerSummary(IHealthController health)
        {
            if (health == null) return "No health controller.";
            try
            {
                int s = Count(health, Started, "effectstarted");
                int r = Count(health, Removed, "effectremoved");
                if (s < 0 && r < 0) return "Effect handler counts unavailable for " + health.GetType().Name + ".";
                return $"Effect handlers: started {s}, removed {r}.";
            }
            catch (Exception e)
            {
                return "Effect handler count failed: " + e.GetType().Name;
            }
        }

        private static int Count(object target, Dictionary<Type, FieldInfo> cache, string fragment)
        {
            Type type = target.GetType();
            if (!cache.TryGetValue(type, out FieldInfo field))
            {
                field = Find(type, fragment);
                cache[type] = field;
            }
            if (field == null) return -1;
            var d = field.GetValue(target) as Delegate;
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static FieldInfo Find(Type type, string fragment)
        {
            // Field-like events keep the event's name (ActiveHealthController: EffectStartedEvent).
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(Declared))
                {
                    if (f.FieldType == typeof(Action<IHealthEffect>) &&
                        f.Name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                        return f;
                }
            }
            // Obfuscated ones (OfflineHealthController: action_3) are found through the field the
            // event's add accessor loads.
            string eventName = fragment.StartsWith("effectstarted") ? "EffectStartedEvent" : "EffectRemovedEvent";
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                EventInfo ev = t.GetEvent(eventName, Declared);
                MethodInfo add = ev?.GetAddMethod(true);
                byte[] il = add?.GetMethodBody()?.GetILAsByteArray();
                if (il == null) continue;
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x7B && il[i] != 0x7C) continue;   // ldfld / ldflda
                    FieldInfo f;
                    try { f = add.Module.ResolveField(BitConverter.ToInt32(il, i + 1)); }
                    catch { continue; }
                    if (f != null && f.FieldType == typeof(Action<IHealthEffect>) && f.DeclaringType.IsAssignableFrom(type))
                        return f;
                }
            }
            return null;
        }
    }
}
