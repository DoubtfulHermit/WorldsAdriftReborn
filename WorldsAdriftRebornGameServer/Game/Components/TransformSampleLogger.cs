using System.Reflection;
using System.Runtime.InteropServices;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components
{
    /// <summary>
    /// Decodes a sample of relayed TransformState (190602) payloads and logs
    /// their content - Parent, LocalPosition, everything - so the logs answer
    /// "what does the sender actually publish" instead of us hypothesising.
    ///
    /// Uses the same vtable deserialize / dereference / destroy pattern as
    /// ComponentUpdateManager.HandleComponentUpdate. Reflection-dumps the
    /// deserialized object rather than binding to its type, so this logs every
    /// field even if our assumptions about the Update layout are wrong.
    ///
    /// Never touches ComponentDatabase directly (static-init trap) - it goes
    /// through ComponentsManager's vtables, which exist only after the game has
    /// populated the database itself.
    /// </summary>
    internal static class TransformSampleLogger
    {
        private const uint TransformStateId = 190602;
        private const int InitialSamples = 3;
        private const int SampleEvery = 500;

        private static int seen;

        public static unsafe void MaybeLog(uint componentId, byte* data, int dataLength)
        {
            if (componentId != TransformStateId || data == null || dataLength <= 0)
            {
                return;
            }

            seen++;
            if (seen > InitialSamples && seen % SampleEvery != 0)
            {
                return;
            }

            try
            {
                for (int i = 0; i < ComponentsManager.Instance.ClientComponentVtables.Length; i++)
                {
                    if (ComponentsManager.Instance.ClientComponentVtables[i].ComponentId != componentId)
                    {
                        continue;
                    }

                    ComponentProtocol.ClientObject* wrapper = ClientObjects.ObjectAlloc();
                    ComponentProtocol.ClientDeserialize deserialize = Marshal.GetDelegateForFunctionPointer<ComponentProtocol.ClientDeserialize>(ComponentsManager.Instance.ClientComponentVtables[i].Deserialize);

                    if (deserialize(componentId, 1, data, (uint)dataLength, &wrapper))
                    {
                        object update = ClientObjects.Instance.Dereference(wrapper->Reference);
                        Console.WriteLine("[transform-sample #" + seen + "] " + Describe(update));
                        ClientObjects.Instance.DestroyReference(wrapper->Reference);
                    }
                    else
                    {
                        Console.WriteLine("[transform-sample #" + seen + "] deserialize FAILED (" + dataLength + " bytes)");
                    }

                    ClientObjects.ObjectFree(componentId, 1, wrapper);
                    return;
                }
            }
            catch (Exception e)
            {
                // Diagnostics must never take the packet loop down.
                Console.WriteLine("[warning] transform sample logging failed: " + e.Message);
            }
        }

        private static string Describe(object update)
        {
            if (update == null)
            {
                return "<null update object>";
            }

            System.Text.StringBuilder sb = new(update.GetType().FullName + " { ");
            foreach (FieldInfo field in update.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object value = field.GetValue(update);
                sb.Append(field.Name).Append('=').Append(value?.ToString() ?? "null").Append("; ");
            }
            foreach (PropertyInfo prop in update.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                try
                {
                    object value = prop.GetValue(update);
                    sb.Append(prop.Name).Append('=').Append(value?.ToString() ?? "null").Append("; ");
                }
                catch
                {
                    // A throwing getter must not kill the dump.
                }
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
