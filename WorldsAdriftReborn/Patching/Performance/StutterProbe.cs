using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Performance
{
    /// <summary>
    /// STUTTER ATTRIBUTION PROBE. Turns every frame spike into ONE named,
    /// grep-able line ("[WAR][perf] spike ...") and leaves a heartbeat trail
    /// ("[WAR][perf] beat ...") every 30 s, so a stutter - or a crash on the
    /// other player's machine - can be attributed after the fact from
    /// BepInEx/LogOutput.log alone.
    ///
    /// WHAT EACH FIELD MEANS AND WHY IT IS THERE
    ///   dt      - the long frame's length (Time.unscaledDeltaTime).
    ///   ents/ops- entities INSTANTIATED / AddEntityOps RECEIVED during the long
    ///             frame. Entity instantiation is synchronous on the main thread
    ///             (DispatchEventHandler.MakeEntity) and the per-frame budget in
    ///             EntityEventHandler.ProcessDeferred is only checked BETWEEN
    ///             entities, so ONE heavy prefab legally blows any budget.
    ///             Nonzero ents on a spike = the world was still streaming in;
    ///             persistent nonzero ents long after activation = the load
    ///             barrier did NOT hold and this line names the leak.
    ///   comps   - AddComponent dispatches to already-live entities (the
    ///             visualizer enable/require machinery runs per add).
    ///   tmpl    - entity template (prefab) requests; a spike with tmpl>0 and
    ///             ents>0 usually means a synchronous prefab load/compile.
    ///   spatial - milliseconds spent inside ConnectionLifecycle.Update during
    ///             the long frame: the WHOLE SpatialOS main-thread slice (op
    ///             dispatch + entity creation + deferred queue). A big dt with
    ///             SMALL spatial exonerates networking/ECS entirely and points
    ///             at GC, rendering, or another Update.
    ///   gc0/1/2 - GC.CollectionCount deltas across the spike window. gc>0 with
    ///             a large negative heapD = a collection ran; the stutter is
    ///             allocation pressure, not workload.
    ///   heapD   - GC.GetTotalMemory delta across the spike window (signed);
    ///             heap = absolute after the spike. A sawtooth of big positive
    ///             heapD between spikes identifies the allocator.
    ///   q       - entity creation backlog (ops received minus entities
    ///             created); a growing q means the client cannot keep up with
    ///             the server's AddEntity pacing.
    ///   thr     - OS thread count of the process (the ~40-worker spin shows up
    ///             here on any machine, including the Windows one).
    ///
    /// TIMING MODEL. Counters are snapshotted every LateUpdate. The dt observed
    /// in frame N is the DURATION OF FRAME N-1, so a spike reports the counter
    /// deltas captured at the END of frame N-1 (_prev*) - the long frame's own
    /// work, not the current frame's.
    ///
    /// COST DISCIPLINE. The happy path allocates NOTHING: integer reads, two GC
    /// API calls (both allocation-free) and float compares. Strings are built
    /// only on a spike/beat/activation line, into a cached StringBuilder, and
    /// spike lines are rate-limited (max 6 per 5 s window; suppressed spikes are
    /// counted and reported in the next beat). The probe must never cause what
    /// it measures.
    ///
    /// All hooks are armed individually with per-hook try/catch: a machine where
    /// one internal type is missing loses that ONE field and says so at boot
    /// ("[WAR][perf] probe armed hooks=..."), instead of losing the probe - or
    /// the mod's whole PatchAll.
    /// </summary>
    internal class StutterProbe : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // Counters. Written on the Unity main thread only: SpatialOS dispatches
        // ops from ConnectionLifecycle.Update, entity creation happens in
        // ProcessDeferred on the same call stack. Plain ints are safe.
        // ------------------------------------------------------------------
        internal static int EntityOps;          // AddEntityOps received (queued)
        internal static int EntityAdds;         // entities actually instantiated
        internal static int ComponentAdds;      // AddComponent dispatches
        internal static int TemplateRequests;   // prefab/template requests
        internal static long SpatialTicks;      // Stopwatch ticks inside ConnectionLifecycle.Update

        private static readonly System.Diagnostics.Stopwatch SpatialWatch = new System.Diagnostics.Stopwatch();

        // Lifetime totals (never reset; the beat prints them so the LAST beat
        // before a crash carries the whole session's shape).
        private static int _totalEntityOps;
        private static int _totalEntityAdds;
        private static int _totalComponentAdds;
        private static int _totalTemplateRequests;

        // ------------------------------------------------------------------
        // Probe state
        // ------------------------------------------------------------------
        private BepInEx.Logging.ManualLogSource _log;
        private readonly StringBuilder _sb = new StringBuilder(320);
        private System.Diagnostics.Process _process;

        private float _thresholdSec = 0.1f;

        // previous frame's activity (the long frame's own work; see TIMING MODEL)
        private int _prevEntityOps, _prevEntityAdds, _prevComponentAdds, _prevTemplateRequests;
        private double _prevSpatialMs;

        // GC bookkeeping
        private int _lastGc0, _lastGc1, _lastGc2;
        private long _lastHeap;

        // spike rate limiting + beat window
        private float _windowStart;
        private int _spikesThisWindow;
        private int _suppressedSinceBeat;
        private int _spikesSinceBeat;
        private float _worstDtSinceBeat;
        private float _lastBeatAt;
        private int _framesSinceBeat;
        private const float BeatPeriodSec = 30f;
        private const int MaxSpikeLinesPerWindow = 6;
        private const float SpikeWindowSec = 5f;

        private void Awake()
        {
            try
            {
                _log = BepInEx.Logging.Logger.CreateLogSource("WAReborn.Perf");
            }
            catch (Exception) { /* fall back to Debug.Log in Say() */ }

            int thresholdMs = 100;
            try
            {
                if (Config.ModSettings.perfSpikeThresholdMs != null)
                {
                    thresholdMs = Config.ModSettings.perfSpikeThresholdMs.Value;
                }
            }
            catch (Exception) { }
            if (thresholdMs < 20) { thresholdMs = 20; } // below ~1.5 frames it would spam
            _thresholdSec = thresholdMs / 1000f;

            try { _process = System.Diagnostics.Process.GetCurrentProcess(); }
            catch (Exception) { }

            _lastGc0 = GC.CollectionCount(0);
            _lastGc1 = GC.CollectionCount(1);
            _lastGc2 = GC.CollectionCount(2);
            _lastHeap = GC.GetTotalMemory(false);
            _windowStart = Time.realtimeSinceStartup;
            _lastBeatAt = Time.realtimeSinceStartup;

            string hooks = ArmHooks();
            Say("[WAR][perf] probe armed threshold=" + thresholdMs + "ms hooks=" + hooks
                + " (spike lines rate-limited to " + MaxSpikeLinesPerWindow + "/" + (int)SpikeWindowSec
                + "s; beat every " + (int)BeatPeriodSec + "s)");
        }

        /// <summary>
        /// Arm each Harmony hook independently. A miss costs one field, never
        /// the probe. Manual patching (no [HarmonyPatch] attributes) so the
        /// mod's CreateAndPatchAll never sees these classes and a resolution
        /// failure here cannot abort the mod's own patch pass.
        /// </summary>
        private string ArmHooks()
        {
            Harmony harmony = new Harmony("com.WAR.WorldsAdriftReborn.perfprobe");
            StringBuilder armed = new StringBuilder(96);

            ArmOne(harmony, armed, "ops",
                "Improbable.Unity.Core.EntityEventHandler", "OnAddEntity",
                null, AccessTools.Method(typeof(StutterProbe), "EntityOp_Postfix"));

            ArmOne(harmony, armed, "ents",
                "Improbable.Unity.Core.DispatchEventHandler", "AddEntity",
                null, AccessTools.Method(typeof(StutterProbe), "EntityAdd_Postfix"));

            ArmOne(harmony, armed, "comps",
                "Improbable.Unity.Core.EntityEventHandler", "OnComponentAdded",
                null, AccessTools.Method(typeof(StutterProbe), "ComponentAdd_Postfix"));

            ArmOne(harmony, armed, "spatial",
                "Improbable.Unity.Core.ConnectionLifecycle", "Update",
                AccessTools.Method(typeof(StutterProbe), "Spatial_Prefix"),
                AccessTools.Method(typeof(StutterProbe), "Spatial_Postfix"));

            ArmOne(harmony, armed, "tmpl",
                "WorkerSpecificAssetDatabaseTemplateProvider", "GetEntityTemplate",
                null, AccessTools.Method(typeof(StutterProbe), "Template_Postfix"));

            // Loading-screen release marker: PlayerActivationVisualiser.Activate
            // fires when 190002 Activated flips. One line per flip timestamps the
            // moment the barrier released the client - every entity instantiated
            // AFTER the isActive=True line streamed in ON SCREEN.
            ArmOne(harmony, armed, "act",
                "PlayerActivationVisualiser", "Activate",
                null, AccessTools.Method(typeof(StutterProbe), "Activation_Postfix"));

            return armed.Length > 0 ? armed.ToString() : "NONE";
        }

        private void ArmOne(Harmony harmony, StringBuilder armed, string name,
            string typeName, string methodName, MethodInfo prefix, MethodInfo postfix)
        {
            try
            {
                Type type = AccessTools.TypeByName(typeName);
                if (type == null) { throw new MissingMemberException(typeName, methodName); }
                MethodBase target = AccessTools.Method(type, methodName);
                if (target == null) { throw new MissingMemberException(typeName, methodName); }
                harmony.Patch(target,
                    prefix == null ? null : new HarmonyMethod(prefix),
                    postfix == null ? null : new HarmonyMethod(postfix));
                if (armed.Length > 0) { armed.Append('+'); }
                armed.Append(name);
            }
            catch (Exception e)
            {
                Say("[WAR][perf] hook '" + name + "' NOT armed (" + typeName + "." + methodName
                    + "): " + e.GetType().Name + " - that field will read 0.");
            }
        }

        // ------------------------------------------------------------------
        // Harmony hook bodies (static, trivial, allocation-free)
        // ------------------------------------------------------------------
        private static void EntityOp_Postfix() { EntityOps++; _totalEntityOps++; }
        private static void EntityAdd_Postfix() { EntityAdds++; _totalEntityAdds++; }
        private static void ComponentAdd_Postfix() { ComponentAdds++; _totalComponentAdds++; }
        private static void Template_Postfix() { TemplateRequests++; _totalTemplateRequests++; }

        private static void Spatial_Prefix()
        {
            SpatialWatch.Reset();
            SpatialWatch.Start();
        }

        private static void Spatial_Postfix()
        {
            SpatialWatch.Stop();
            SpatialTicks += SpatialWatch.ElapsedTicks;
        }

        private static void Activation_Postfix(bool isActive)
        {
            // Rare event (once per barrier release / reset); direct log is fine.
            Debug.Log("[WAR][perf] activation isActive=" + isActive
                + " t=" + Time.realtimeSinceStartup.ToString("0.0", CultureInfo.InvariantCulture)
                + "s f=" + Time.frameCount
                + " entsSoFar=" + _totalEntityAdds + " opsSoFar=" + _totalEntityOps);
        }

        // ------------------------------------------------------------------
        // Per-frame evaluation. LateUpdate so every Update (including
        // ConnectionLifecycle's) has already run when we snapshot.
        // ------------------------------------------------------------------
        private void LateUpdate()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Time.unscaledDeltaTime;
            _framesSinceBeat++;

            // GC window: last LateUpdate -> now (covers the long frame).
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            long heap = GC.GetTotalMemory(false);
            int dGc0 = gc0 - _lastGc0;
            int dGc1 = gc1 - _lastGc1;
            int dGc2 = gc2 - _lastGc2;
            long dHeap = heap - _lastHeap;

            if (dt > _worstDtSinceBeat) { _worstDtSinceBeat = dt; }

            if (dt >= _thresholdSec)
            {
                if (now - _windowStart > SpikeWindowSec)
                {
                    _windowStart = now;
                    _spikesThisWindow = 0;
                }
                _spikesSinceBeat++;
                if (_spikesThisWindow < MaxSpikeLinesPerWindow)
                {
                    _spikesThisWindow++;
                    EmitSpike(dt, now, dGc0, dGc1, dGc2, dHeap, heap);
                }
                else
                {
                    _suppressedSinceBeat++;
                }
            }

            if (now - _lastBeatAt >= BeatPeriodSec)
            {
                EmitBeat(now, heap);
                _lastBeatAt = now;
                _framesSinceBeat = 0;
                _spikesSinceBeat = 0;
                _suppressedSinceBeat = 0;
                _worstDtSinceBeat = 0f;
            }

            // Roll this frame's activity into "previous frame" storage and reset.
            _prevEntityOps = EntityOps;
            _prevEntityAdds = EntityAdds;
            _prevComponentAdds = ComponentAdds;
            _prevTemplateRequests = TemplateRequests;
            _prevSpatialMs = SpatialTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            EntityOps = 0;
            EntityAdds = 0;
            ComponentAdds = 0;
            TemplateRequests = 0;
            SpatialTicks = 0;

            _lastGc0 = gc0;
            _lastGc1 = gc1;
            _lastGc2 = gc2;
            _lastHeap = heap;
        }

        private void EmitSpike(float dt, float now, int dGc0, int dGc1, int dGc2, long dHeap, long heap)
        {
            _sb.Length = 0;
            _sb.Append("[WAR][perf] spike dt=");
            _sb.Append((dt * 1000f).ToString("0.0", CultureInfo.InvariantCulture));
            _sb.Append("ms f=").Append(Time.frameCount);
            _sb.Append(" t=").Append(now.ToString("0.0", CultureInfo.InvariantCulture)).Append('s');
            _sb.Append(" ents+").Append(_prevEntityAdds);
            _sb.Append("/ops+").Append(_prevEntityOps);
            _sb.Append(" comps+").Append(_prevComponentAdds);
            _sb.Append(" tmpl+").Append(_prevTemplateRequests);
            _sb.Append(" spatial=").Append(_prevSpatialMs.ToString("0.0", CultureInfo.InvariantCulture)).Append("ms");
            _sb.Append(" gc0+").Append(dGc0).Append(" gc1+").Append(dGc1).Append(" gc2+").Append(dGc2);
            _sb.Append(" heapD=").Append((dHeap / (1024.0 * 1024.0)).ToString("+0.0;-0.0", CultureInfo.InvariantCulture)).Append("MB");
            _sb.Append(" heap=").Append((heap / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture)).Append("MB");
            _sb.Append(" q=").Append(_totalEntityOps - _totalEntityAdds);
            _sb.Append(" thr=").Append(ThreadCount());
            Say(_sb.ToString());
        }

        private void EmitBeat(float now, long heap)
        {
            float period = now - _lastBeatAt;
            float fps = period > 0f ? _framesSinceBeat / period : 0f;
            _sb.Length = 0;
            _sb.Append("[WAR][perf] beat t=");
            _sb.Append(now.ToString("0.0", CultureInfo.InvariantCulture)).Append('s');
            _sb.Append(" f=").Append(Time.frameCount);
            _sb.Append(" fps=").Append(fps.ToString("0.0", CultureInfo.InvariantCulture));
            _sb.Append(" spikes=").Append(_spikesSinceBeat);
            _sb.Append(" supp=").Append(_suppressedSinceBeat);
            _sb.Append(" worst=").Append((_worstDtSinceBeat * 1000f).ToString("0.0", CultureInfo.InvariantCulture)).Append("ms");
            _sb.Append(" ents=").Append(_totalEntityAdds);
            _sb.Append("/ops=").Append(_totalEntityOps);
            _sb.Append(" comps=").Append(_totalComponentAdds);
            _sb.Append(" tmpl=").Append(_totalTemplateRequests);
            _sb.Append(" gc0=").Append(GC.CollectionCount(0));
            _sb.Append(" gc1=").Append(GC.CollectionCount(1));
            _sb.Append(" gc2=").Append(GC.CollectionCount(2));
            _sb.Append(" heap=").Append((heap / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture)).Append("MB");
            _sb.Append(" thr=").Append(ThreadCount());
            Say(_sb.ToString());
        }

        private int ThreadCount()
        {
            // Only called on spike/beat lines. Refresh + Threads snapshots the
            // process (allocates); never on the happy path.
            try
            {
                if (_process == null) { return -1; }
                _process.Refresh();
                return _process.Threads.Count;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private void Say(string message)
        {
            if (_log != null)
            {
                _log.LogInfo(message);
            }
            else
            {
                Debug.Log(message);
            }
        }
    }
}
