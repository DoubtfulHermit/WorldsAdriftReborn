using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Automation
{
    /// <summary>
    /// Main-thread semantic input overlay used only by LocalTestBridge.
    /// Physical input remains authoritative when no synthetic value exists.
    /// Button edges are consumed once, matching Input.GetButtonDown/Up instead
    /// of becoming frame-rate-dependent repeated presses.
    /// </summary>
    internal static class SyntheticInput
    {
        private sealed class ButtonState
        {
            internal bool Held;
            internal bool DownPending;
            internal bool UpPending;
            internal int AutoReleaseFrame = -1;
        }

        private static readonly Dictionary<InputButtons, ButtonState> Buttons =
            new Dictionary<InputButtons, ButtonState>();
        private static readonly Dictionary<InputAxes, float> Axes =
            new Dictionary<InputAxes, float>();
        private static bool _enabled;

        internal static void Enable()
        {
            Clear();
            _enabled = true;
        }

        internal static void Disable()
        {
            _enabled = false;
            Clear();
        }

        internal static void Tap(InputButtons button)
        {
            ButtonState state = StateFor(button);
            state.Held = true;
            state.DownPending = true;
            state.UpPending = false;
            state.AutoReleaseFrame = Time.frameCount + 2;
        }

        internal static void Hold(InputButtons button, bool held)
        {
            ButtonState state = StateFor(button);
            if (state.Held == held)
                return;
            state.Held = held;
            state.DownPending = held;
            state.UpPending = !held;
            state.AutoReleaseFrame = -1;
        }

        internal static void SetAxis(InputAxes axis, float value)
        {
            Axes[axis] = Mathf.Clamp(value, -1f, 1f);
        }

        internal static void ClearAxis(InputAxes axis)
        {
            Axes.Remove(axis);
        }

        internal static void Clear()
        {
            Buttons.Clear();
            Axes.Clear();
        }

        internal static void Tick()
        {
            foreach (KeyValuePair<InputButtons, ButtonState> pair in Buttons)
            {
                ButtonState state = pair.Value;
                if (state.Held && state.AutoReleaseFrame >= 0
                    && Time.frameCount >= state.AutoReleaseFrame)
                {
                    state.Held = false;
                    state.UpPending = true;
                    state.AutoReleaseFrame = -1;
                }
            }
        }

        internal static bool TryHeld(InputSink sink, InputButtons button, out bool value)
        {
            ButtonState state;
            if (!_enabled || !Buttons.TryGetValue(button, out state)
                || !state.Held || !sink.CanReceive(button))
            {
                value = false;
                return false;
            }
            value = state.Held;
            return true;
        }

        internal static bool TryDown(InputSink sink, InputButtons button, out bool value)
        {
            ButtonState state;
            if (!_enabled || !Buttons.TryGetValue(button, out state)
                || !state.DownPending || !sink.CanReceive(button))
            {
                value = false;
                return false;
            }
            value = true;
            state.DownPending = false;
            return true;
        }

        internal static bool TryUp(InputSink sink, InputButtons button, out bool value)
        {
            ButtonState state;
            if (!_enabled || !Buttons.TryGetValue(button, out state)
                || !state.UpPending || !sink.CanReceive(button))
            {
                value = false;
                return false;
            }
            value = true;
            state.UpPending = false;
            if (!state.Held)
                Buttons.Remove(button);
            return true;
        }

        internal static bool TryAxis(InputSink sink, InputAxes axis, out float value)
        {
            value = 0f;
            return _enabled && sink.CanReceive(axis) && Axes.TryGetValue(axis, out value);
        }

        private static ButtonState StateFor(InputButtons button)
        {
            ButtonState state;
            if (!Buttons.TryGetValue(button, out state))
            {
                state = new ButtonState();
                Buttons.Add(button, state);
            }
            return state;
        }
    }

    [HarmonyPatch(typeof(InputSink), "GetButton")]
    internal static class SyntheticGetButtonPatch
    {
        private static bool Prefix(InputSink __instance, InputButtons button, ref bool __result)
        {
            return !SyntheticInput.TryHeld(__instance, button, out __result);
        }
    }

    [HarmonyPatch(typeof(InputSink), "GetButtonDown")]
    internal static class SyntheticGetButtonDownPatch
    {
        private static bool Prefix(InputSink __instance, InputButtons button, ref bool __result)
        {
            return !SyntheticInput.TryDown(__instance, button, out __result);
        }
    }

    [HarmonyPatch(typeof(InputSink), "GetButtonUp")]
    internal static class SyntheticGetButtonUpPatch
    {
        private static bool Prefix(InputSink __instance, InputButtons button, ref bool __result)
        {
            return !SyntheticInput.TryUp(__instance, button, out __result);
        }
    }

    [HarmonyPatch(typeof(InputSink), "GetAxis")]
    internal static class SyntheticGetAxisPatch
    {
        private static bool Prefix(InputSink __instance, InputAxes axis, ref float __result)
        {
            return !SyntheticInput.TryAxis(__instance, axis, out __result);
        }
    }
}
