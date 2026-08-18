using System.Reflection;
using HarmonyLib;
using Travellers.UI.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace WorldsAdriftReborn.Patching.Social
{
    /// <summary>
    /// Makes the social sheet's busy spinner turn at a fixed speed instead of a
    /// fixed speed PER FRAME, and stop snapping back to a third of a turn every
    /// time one request finishes and the next begins.
    ///
    /// Reported as "the spinning icon kinda speeds super fast and jumps to crew
    /// created, it looks not natural" after creating a crew. Nothing about it is
    /// server-side, and this file exists because that was worth establishing
    /// before looking for a fix in the wrong place:
    /// docs/research/findings-social-api.md records the diagnosis.
    ///
    /// TWO defects, and both have to go or the symptom only halves.
    ///
    /// 1. FRAME-RATE COUPLING. ForwardSpin.Spin is
    ///
    ///        spinImage.fillAmount += 0.02f;
    ///
    ///    called from LoadingInputBlocker.Update. No Time.deltaTime anywhere in
    ///    the chain, so a full sweep is exactly 50 FRAMES: 0.83 s at the 60 FPS it
    ///    was authored for, 0.25 s at 200, 0.17 s at 300. On a modern machine the
    ///    wheel is simply spinning three to five times too fast. The same codebase
    ///    ships a correct time-based version of the identical effect,
    ///    SpinningSprite, which this component does not use.
    ///
    /// 2. A RESET PER ROUND TRIP. LoadingInputBlocker.Activate sets fillAmount
    ///    back to 0.3333f, and Activate runs on every off-to-on transition of the
    ///    overlay. "Busy" is a boolean edge from SocialRequestMonitor - raised when
    ///    its in-flight dictionary becomes non-empty, dropped when it empties - and
    ///    the post-create calls are strictly sequential (POST crews, then
    ///    memberships/character, crew/{region}/{uid}, memberships/crew/{uid},
    ///    memberships/invites/crew/{uid}, each chained on the last). The dictionary
    ///    empties between every one, so the wheel is yanked back to a third of a
    ///    turn four times in under a second, on top of already spinning too fast.
    ///
    /// Then YouAsLeaderState.EnterScreen flips the panel with bare SetActive calls
    /// and no transition, which is the "jumps to crew created". That last part is
    /// left alone: it is how every state in that screen is entered, and animating
    /// one of them would be a change to the game's look rather than a repair.
    ///
    /// Cosmetic and unconditional. Deliberately NOT behind a config key - a new
    /// BepInEx key materialises into every existing player's config file with its
    /// shipped default, which is exactly how the REST_AlliancesUrl incident broke
    /// every install, and a spinner is not worth that risk.
    /// </summary>
    [HarmonyPatch]
    internal static class LoadingSpinner_Patch
    {
        /// <summary>
        /// The authored speed, in fill per SECOND.
        ///
        /// 0.02 per frame at the 60 FPS the original targets, so 1.2 - not a
        /// number picked to look right. The intent was a sweep every 0.83 s and
        /// this reproduces exactly that, independent of frame rate.
        /// </summary>
        private const float FillPerSecond = 0.02f * 60f;

        /// <summary>
        /// A ceiling on one step, so a frame hitch or a breakpoint cannot make the
        /// wheel jump most of a turn. 0.05 is three frames' worth at 60 FPS: long
        /// enough never to bite in normal play, short enough that a two-second
        /// stall does not present as the wheel teleporting.
        /// </summary>
        private const float MaxStep = 0.05f;

        private static float Step()
        {
            float step = FillPerSecond * Time.deltaTime;
            return step > MaxStep ? MaxStep : step;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ForwardSpin), nameof(ForwardSpin.Spin))]
        private static bool ForwardSpin_Spin_Prefix(Image spinImage)
        {
            if (spinImage == null) return false;

            spinImage.fillClockwise = true;
            spinImage.fillAmount += Step();

            // Skips the original entirely. A postfix would have to undo the
            // original's own += 0.02f first, which is a worse way to say the same
            // thing and would drift if that constant ever changed.
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ReverseSpin), nameof(ReverseSpin.Spin))]
        private static bool ReverseSpin_Spin_Prefix(Image spinImage)
        {
            if (spinImage == null) return false;

            spinImage.fillClockwise = false;
            spinImage.fillAmount -= Step();
            return false;
        }

        private static readonly FieldInfo SpinPhaseField =
            AccessTools.Field(typeof(LoadingInputBlocker), "_spinPhase");

        /// <summary>
        /// Shows the overlay without rewinding the wheel.
        ///
        /// The original's whole body is the two lines this replaces, so skipping it
        /// is safe - but ONE of them cannot simply be dropped. Activate is also the
        /// only thing that guarantees _spinPhase is non-null before Update reads
        /// it; ProtectedInit sets it too, and if a blocker were ever enabled before
        /// its init ran, removing Activate's assignment outright would turn a
        /// cosmetic bug into a NullReferenceException every frame. So the phase is
        /// still ensured, and only the fillAmount rewind - the part the player
        /// actually sees - is dropped.
        ///
        /// The phase is preserved rather than reset as well, so a wheel caught
        /// mid-return does not reverse direction on top of everything else.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(LoadingInputBlocker), "Activate")]
        private static bool LoadingInputBlocker_Activate_Prefix(LoadingInputBlocker __instance)
        {
            if (SpinPhaseField != null && SpinPhaseField.GetValue(__instance) == null)
            {
                SpinPhaseField.SetValue(__instance, new ForwardSpin());
            }

            return false;
        }
    }
}
