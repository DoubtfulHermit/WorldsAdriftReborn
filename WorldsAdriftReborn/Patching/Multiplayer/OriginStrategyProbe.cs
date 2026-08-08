using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * Reports which floating-origin strategy is actually live, and what the
     * origin offset currently is. READ-ONLY: it never calls anything that can
     * move the origin.
     *
     * WHY THIS EXISTS
     * ---------------
     * SpatialOS re-bases Unity's origin as the player moves. The machinery is:
     *
     *   Improbable.CoreLibrary.CoordinateRemapping.CoordinateRemappingBehaviour
     *     - static GetDetermineOriginStrategy()   -> the live IDetermineOriginStrategy
     *     - static OffsetOrigin (Vector3d)        -> the current world-space origin
     *     - Awake() picks the strategy from GetComponents<IDetermineOriginStrategy>()
     *       on its OWN GameObject, else adds a default.
     *
     * Because the choice is made from SCENE-SERIALIZED components, the decompile
     * cannot tell us which one is in the shipped scene. Candidates found in the
     * client (see the report in docs/research/findings-spawn.md):
     *
     *   Bossa.Travellers.Remapping.ActiveIslandBasedRemapping
     *       origin := the nearest checked-out island's global position, re-checked
     *       every `checkSeconds` (default 5) starting at `nextCheckTime` (10).
     *   Bossa.Travellers.Remapping.RemapBasedOnPlayerPos
     *       origin := player's global position whenever the player's UNITY position
     *       exceeds sqrt(sqrRemapThreshold) = 1500 m from the Unity origin.
     *   Assets.Scripts.Remapping.BossaEntityBoundsStrategy
     *   Improbable...DetermineOrigin.EntityBoundsReactiveDetermineOriginStrategy
     *       origin := first converted global position, then the midpoint of all
     *       entity bounds once anything exceeds 50,000 from the Unity origin.
     *   FSimRemapOnceDetermineOriginStrategy
     *       origin := first converted global position, then never again.
     *   Improbable...CoordinateRemapping.NullDetermineOriginStrategy
     *       identity. This is ALSO the fallback GetDetermineOriginStrategy()
     *       returns when there is no CoordinateRemappingBehaviour in the scene at
     *       all, so this probe distinguishes the two (see crb= in the output).
     *
     * With the island parked at the world origin every one of these behaves
     * identically, so the sessions captured so far cannot discriminate. At
     * Haven's real position (~17 km out) they do not, which is why this must be
     * run BEFORE the island is moved.
     *
     * WHAT IT PRINTS  (grep the log for "ORIGIN")
     *   ORIGIN <event> strategy=<concrete type> crb=<...> remap=IDENTITY|REBASED
     *          offsetOrigin=(x, y, z) player.unity=(x, y, z) player.world=(x, y, z)
     *          unityDist=<m>
     *   plus, once and on every change, a "ORIGIN detail" line with the strategy's
     *   tuning fields and the sibling components on its GameObject.
     *
     * player.world is computed as player.unity + offsetOrigin, which is exactly
     * AbstractDetermineOriginStrategy.UnityPositionToGlobalPosition. It is
     * deliberately NOT obtained by calling that method: on the reactive
     * strategies that call has a SIDE EFFECT (QueueRecalculationIfNecessary can
     * start an origin-recalculation coroutine), and this probe must not perturb
     * what it measures.
     *
     * Everything is resolved by reflection and every failure logs a warning
     * instead of throwing, so a wrong guess about a name can never break the
     * mod's load.
     *
     * Output goes through BepInEx's own logger, so it reaches
     * BepInEx/LogOutput.log even if BepInEx.cfg has WriteUnityLog = false.
     * It falls back to Debug.Log only if the log source cannot be created.
     */
    internal class OriginStrategyProbe : MonoBehaviour
    {
        private const KeyCode ReportKey = KeyCode.F10;

        // Slow repeat. Cheap enough to poll for changes far more often than we
        // print a heartbeat.
        private const float HeartbeatSeconds = 5f;
        private const float PollSeconds = 0.25f;

        private const string CoordinateRemappingBehaviourType =
            "Improbable.CoreLibrary.CoordinateRemapping.CoordinateRemappingBehaviour";

        private BepInEx.Logging.ManualLogSource log;

        private bool resolveAttempted;
        private Type crbType;
        private MethodInfo getStrategyMethod;
        private PropertyInfo liveStrategyProperty;   // protected static, null => no CRB in scene
        private PropertyInfo staticOffsetOriginProperty;

        private float nextPoll;
        private float nextHeartbeat;

        private bool haveLast;
        private string lastTypeName;
        private double lastX, lastY, lastZ;

        private void Awake()
        {
            try
            {
                log = BepInEx.Logging.Logger.CreateLogSource("WAReborn.OriginProbe");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] ORIGIN probe: could not create a BepInEx log source ("
                    + e.Message + "); falling back to Debug.Log.");
            }
            Say("ORIGIN probe armed. Reports automatically once the world is up; F10 forces a report.");
        }

        private void Update()
        {
            try
            {
                if (Input.GetKeyDown(ReportKey))
                {
                    Report("manual", true);
                    return;
                }

                if (Time.unscaledTime < nextPoll)
                {
                    return;
                }
                nextPoll = Time.unscaledTime + PollSeconds;

                if (!WorldIsUp())
                {
                    return;
                }

                object strategy = GetStrategy();
                string typeName = strategy == null ? "<null>" : strategy.GetType().FullName;
                double x = 0d, y = 0d, z = 0d;
                bool haveOrigin = TryReadOffsetOrigin(strategy, ref x, ref y, ref z);

                bool changed = !haveLast
                    || typeName != lastTypeName
                    || (haveOrigin && (x != lastX || y != lastY || z != lastZ));

                if (changed)
                {
                    string why = !haveLast ? "first" : (typeName != lastTypeName ? "STRATEGY-CHANGED" : "ORIGIN-MOVED");
                    if (haveLast && why == "ORIGIN-MOVED")
                    {
                        Say("ORIGIN moved by (" + Fmt(x - lastX) + ", " + Fmt(y - lastY) + ", " + Fmt(z - lastZ)
                            + ") - every unparented transform in the scene was shifted by the inverse.");
                    }
                    Report(why, true);
                    haveLast = true;
                    lastTypeName = typeName;
                    lastX = x; lastY = y; lastZ = z;
                    nextHeartbeat = Time.unscaledTime + HeartbeatSeconds;
                    return;
                }

                if (Time.unscaledTime >= nextHeartbeat)
                {
                    nextHeartbeat = Time.unscaledTime + HeartbeatSeconds;
                    Report("heartbeat", false);
                }
            }
            catch (Exception e)
            {
                // Never let a diagnostic take the frame down.
                Warn("ORIGIN probe: Update threw, disabling: " + e);
                enabled = false;
            }
        }

        /*
         * "The world is actually up" = either the CoordinateRemappingBehaviour has
         * run its Awake (so a strategy has been selected), or the local rig exists.
         * The second half matters because it is the case where NO
         * CoordinateRemappingBehaviour is in the scene at all - which is itself a
         * result worth printing, and which the first half alone would hide forever.
         */
        private bool WorldIsUp()
        {
            if (ReadLiveStrategyField() != null)
            {
                return true;
            }
            return CameraProxy_Patch.OwnerRoot != null;
        }

        private void Report(string why, bool withDetail)
        {
            try
            {
                object strategy = GetStrategy();
                if (strategy == null)
                {
                    Warn("ORIGIN " + why + ": could not resolve a strategy at all "
                        + "(CoordinateRemappingBehaviour type "
                        + (crbType == null ? "NOT FOUND" : "found") + ").");
                    return;
                }

                double ox = 0d, oy = 0d, oz = 0d;
                bool haveOrigin = TryReadOffsetOrigin(strategy, ref ox, ref oy, ref oz);

                object live = ReadLiveStrategyField();
                string crb;
                if (live == null)
                {
                    crb = "ABSENT(no CoordinateRemappingBehaviour in scene - this is the "
                        + "NullDetermineOriginStrategy FALLBACK, not a scene component)";
                }
                else
                {
                    Component asComponent = live as Component;
                    crb = asComponent != null
                        ? "component-on:'" + SafeName(asComponent.gameObject) + "'"
                        : "present(not-a-Component)";
                }

                StringBuilder sb = new StringBuilder();
                sb.Append("ORIGIN ").Append(why);
                sb.Append(" strategy=").Append(strategy.GetType().FullName);
                sb.Append(" crb=").Append(crb);
                if (haveOrigin)
                {
                    bool identity = ox == 0d && oy == 0d && oz == 0d;
                    sb.Append(" remap=").Append(identity ? "IDENTITY" : "REBASED");
                    sb.Append(" offsetOrigin=(").Append(Fmt(ox)).Append(", ")
                        .Append(Fmt(oy)).Append(", ").Append(Fmt(oz)).Append(")");
                }
                else
                {
                    sb.Append(" remap=UNKNOWN offsetOrigin=<unreadable>");
                }

                Transform player = GetPlayerRoot();
                if (player == null)
                {
                    sb.Append(" player=<none yet>");
                }
                else
                {
                    Vector3 u = player.position;
                    sb.Append(" player.unity=(").Append(Fmt(u.x)).Append(", ")
                        .Append(Fmt(u.y)).Append(", ").Append(Fmt(u.z)).Append(")");
                    if (haveOrigin)
                    {
                        // == AbstractDetermineOriginStrategy.UnityPositionToGlobalPosition,
                        // without its side effects. See the header comment.
                        sb.Append(" player.world=(").Append(Fmt(u.x + ox)).Append(", ")
                            .Append(Fmt(u.y + oy)).Append(", ").Append(Fmt(u.z + oz)).Append(")");
                    }
                    sb.Append(" unityDist=").Append(Fmt(u.magnitude));
                    sb.Append(" rig='").Append(SafeName(player.gameObject)).Append("'");
                }

                Say(sb.ToString());

                if (withDetail)
                {
                    ReportDetail(strategy, live);
                }
            }
            catch (Exception e)
            {
                Warn("ORIGIN " + why + ": report threw: " + e);
            }
        }

        /*
         * The tuning that decides WHEN the origin next moves. On
         * ActiveIslandBasedRemapping this also exposes the current active island;
         * on RemapBasedOnPlayerPos, sqrRemapThreshold (default 2,250,000 = 1500 m);
         * on the reactive strategies, DistanceFromOriginToTriggerOriginCalculation
         * (default 50,000).
         */
        private void ReportDetail(object strategy, object live)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("ORIGIN detail fields:");

            int printed = 0;
            for (Type t = strategy.GetType();
                 t != null && t != typeof(MonoBehaviour) && t != typeof(object);
                 t = t.BaseType)
            {
                FieldInfo[] fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    string rendered = RenderValue(fields[i], strategy);
                    if (rendered != null)
                    {
                        sb.Append(' ').Append(fields[i].Name).Append('=').Append(rendered);
                        printed++;
                    }
                }
            }
            if (printed == 0)
            {
                sb.Append(" <none>");
            }

            Component asComponent = live as Component;
            if (asComponent != null)
            {
                sb.Append(" | siblings on '").Append(SafeName(asComponent.gameObject)).Append("':");
                Component[] all = asComponent.GetComponents<Component>();
                for (int i = 0; i < all.Length; i++)
                {
                    sb.Append(' ').Append(all[i] == null ? "<missing>" : all[i].GetType().Name);
                }
            }

            Say(sb.ToString());
        }

        private static string RenderValue(FieldInfo field, object instance)
        {
            try
            {
                object v = field.GetValue(instance);
                if (v == null)
                {
                    return null;
                }
                if (v is float || v is double || v is int || v is uint || v is long || v is bool || v is string)
                {
                    return v.ToString();
                }
                if (v is Vector3)
                {
                    Vector3 vec = (Vector3)v;
                    return "(" + Fmt(vec.x) + ", " + Fmt(vec.y) + ", " + Fmt(vec.z) + ")";
                }
                Component c = v as Component;
                if (c != null)
                {
                    Vector3 p = c.transform.position;
                    return "'" + SafeName(c.gameObject) + "'@unity(" + Fmt(p.x) + ", " + Fmt(p.y) + ", " + Fmt(p.z) + ")";
                }
                UnityEngine.Object o = v as UnityEngine.Object;
                if (o != null)
                {
                    return "'" + o.name + "'";
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        // --- reflection plumbing -------------------------------------------------

        private void Resolve()
        {
            if (resolveAttempted)
            {
                return;
            }
            resolveAttempted = true;

            crbType = FindType(CoordinateRemappingBehaviourType);
            if (crbType == null)
            {
                Warn("ORIGIN probe: type " + CoordinateRemappingBehaviourType
                    + " not found in any loaded assembly. The probe can report nothing.");
                return;
            }

            getStrategyMethod = crbType.GetMethod("GetDetermineOriginStrategy",
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (getStrategyMethod == null)
            {
                Warn("ORIGIN probe: CoordinateRemappingBehaviour.GetDetermineOriginStrategy() not found.");
            }

            // Protected static. Null here means CoordinateRemappingBehaviour.Awake
            // never ran, i.e. there is no such behaviour in the scene - in which case
            // GetDetermineOriginStrategy() hands back a Null strategy FALLBACK and the
            // remap is permanently the identity. Distinguishing those two is the whole
            // point of reading this.
            liveStrategyProperty = crbType.GetProperty("DetermineOriginStrategy",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (liveStrategyProperty == null)
            {
                Warn("ORIGIN probe: CoordinateRemappingBehaviour.DetermineOriginStrategy property not found; "
                    + "cannot tell a scene strategy from the null fallback.");
            }

            staticOffsetOriginProperty = crbType.GetProperty("OffsetOrigin",
                BindingFlags.Public | BindingFlags.Static);
            if (staticOffsetOriginProperty == null)
            {
                Warn("ORIGIN probe: CoordinateRemappingBehaviour.OffsetOrigin property not found.");
            }
        }

        private object GetStrategy()
        {
            Resolve();
            if (getStrategyMethod == null)
            {
                return ReadLiveStrategyField();
            }
            try
            {
                return getStrategyMethod.Invoke(null, null);
            }
            catch (Exception e)
            {
                Warn("ORIGIN probe: GetDetermineOriginStrategy() threw: " + e.Message);
                return null;
            }
        }

        private object ReadLiveStrategyField()
        {
            Resolve();
            if (liveStrategyProperty == null)
            {
                return null;
            }
            try
            {
                return liveStrategyProperty.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }

        /*
         * OffsetOrigin is an Improbable.Math.Vector3d (a struct with public double
         * fields X/Y/Z). Read it off the strategy instance; fall back to the static
         * CoordinateRemappingBehaviour.OffsetOrigin.
         */
        private bool TryReadOffsetOrigin(object strategy, ref double x, ref double y, ref double z)
        {
            object boxed = null;
            if (strategy != null)
            {
                PropertyInfo p = strategy.GetType().GetProperty("OffsetOrigin",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null)
                {
                    try { boxed = p.GetValue(strategy, null); } catch { boxed = null; }
                }
            }
            if (boxed == null)
            {
                Resolve();
                if (staticOffsetOriginProperty != null)
                {
                    try { boxed = staticOffsetOriginProperty.GetValue(null, null); } catch { boxed = null; }
                }
            }
            if (boxed == null)
            {
                return false;
            }

            Type vt = boxed.GetType();
            FieldInfo fx = vt.GetField("X", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo fy = vt.GetField("Y", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo fz = vt.GetField("Z", BindingFlags.Public | BindingFlags.Instance);
            if (fx == null || fy == null || fz == null)
            {
                Warn("ORIGIN probe: " + vt.FullName + " has no public X/Y/Z fields; raw value = " + boxed);
                return false;
            }
            try
            {
                x = Convert.ToDouble(fx.GetValue(boxed));
                y = Convert.ToDouble(fy.GetValue(boxed));
                z = Convert.ToDouble(fz.GetValue(boxed));
                return true;
            }
            catch (Exception e)
            {
                Warn("ORIGIN probe: could not read X/Y/Z off " + vt.FullName + ": " + e.Message);
                return false;
            }
        }

        /*
         * The local rig. LocalPlayer is a scene object and its root is NOT a
         * reliable "this is me" marker here (docs/multiplayer.md rule 11), so the
         * camera-claiming rig is preferred; LocalPlayer.Instance.playerGameObject is
         * only a fallback for the window before the camera is claimed.
         */
        private Transform GetPlayerRoot()
        {
            Transform owner = CameraProxy_Patch.OwnerRoot;
            if (owner != null)
            {
                return owner;
            }
            try
            {
                Type lp = FindType("LocalPlayer");
                if (lp == null)
                {
                    return null;
                }
                PropertyInfo exists = lp.GetProperty("Exists", BindingFlags.Public | BindingFlags.Static);
                if (exists != null && !(bool)exists.GetValue(null, null))
                {
                    return null;
                }
                PropertyInfo instance = lp.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                object inst = instance != null ? instance.GetValue(null, null) : null;
                if (inst == null)
                {
                    return null;
                }
                FieldInfo go = lp.GetField("playerGameObject", BindingFlags.Public | BindingFlags.Instance);
                GameObject obj = go != null ? go.GetValue(inst) as GameObject : null;
                return obj != null ? obj.transform : null;
            }
            catch
            {
                return null;
            }
        }

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        private static Type FindType(string fullName)
        {
            Type cached;
            if (TypeCache.TryGetValue(fullName, out cached))
            {
                return cached;
            }
            Type found = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && found == null; i++)
            {
                try
                {
                    found = assemblies[i].GetType(fullName, false);
                }
                catch
                {
                    // A dynamic or partially-loaded assembly. Skip it.
                }
            }
            if (found != null)
            {
                TypeCache[fullName] = found;
            }
            return found;
        }

        private static string SafeName(GameObject go)
        {
            return go == null ? "<null>" : go.name;
        }

        private static string Fmt(double v)
        {
            return v.ToString("F3");
        }

        private static string Fmt(float v)
        {
            return v.ToString("F3");
        }

        private void Say(string message)
        {
            if (log != null)
            {
                log.LogInfo(message);
            }
            else
            {
                Debug.Log("[WAReborn] " + message);
            }
        }

        private void Warn(string message)
        {
            if (log != null)
            {
                log.LogWarning(message);
            }
            else
            {
                Debug.LogWarning("[WAReborn] " + message);
            }
        }
    }
}
