using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.InGameChanges
{
    /// <summary>
    /// UN-DISCO THE SKY WHALE.
    ///
    /// The animal is finished: 172.88 m long, rigged, skinned, animated, one
    /// material, a looping <c>Whale_Swim</c> clip. The PREFAB is a joke Bossa left
    /// on top of it - it is literally named <c>DiscoWhale</c> - and the joke is
    /// five separate things, every one of which is on by default and none of which
    /// the server can do anything about, because a server sends a prefab NAME:
    ///
    /// <list type="number">
    /// <item>the <c>DiscoWhale</c> MonoBehaviour itself. Every frame it does
    ///   <c>_material.SetColor(_Color, HSVToRGB(t, 1, 0.5))</c> and the same for
    ///   <c>_IllumTint</c> at 1.2x the rate, with <c>_hueRotateRate = 0.1</c> - a
    ///   full hue lap every ten seconds. The material it writes is
    ///   <c>_renderer.sharedMaterial</c>, THE ASSET, not an instance, so it is not
    ///   only that the whale strobes: every whale in the world strobes in lockstep
    ///   off one mutated asset, and the mutation OUTLIVES the whale;</item>
    /// <item>a child point light with a 200 m range, hard shadows and a culling
    ///   mask of everything, driven to the same cycling hue;</item>
    /// <item>a fireworks <c>ParticleSystem</c> on the root, <c>playOnAwake</c> and
    ///   <c>looping</c>, plus two <c>SubFireworks</c> children and the seven
    ///   emitters under them (<c>FireBall_red</c>, <c>FireBall_white</c>,
    ///   <c>ImpactSpikes</c>, <c>sparks</c>, <c>Embers</c>, <c>Heavysmoke</c>,
    ///   <c>CenterGlow</c>);</item>
    /// <item>a stray child called <c>Cube</c> that renders
    ///   <c>tree_e_section_9_LOD0</c> - a five-metre section of tree trunk - in
    ///   <c>Default-Material</c>, at the whale's origin, inside the animal;</item>
    /// <item>and the MonoBehaviour's own coroutine, which posts the Wwise event
    ///   <c>Big_DistantCall</c> on a client-local <c>Random.Range(25f, 45f)</c>.</item>
    /// </list>
    ///
    /// THE FIFTH ONE IS A DELIBERATE LOSS, and it is worth being explicit about
    /// because disabling the behaviour takes it with the rest. That call was
    /// client-local and randomly timed, so two players standing together heard the
    /// same animal call at different moments - it was never a shared world event.
    /// WAReborn drives the call from the server instead, through the <c>BigCall</c>
    /// entity, which is the thing retail actually shipped for it: one call every
    /// two minutes, from where the animal WAS, to everyone within four kilometres
    /// at once. So the whale is not silenced; its voice moved somewhere every
    /// player hears the same thing.
    ///
    /// WHY <c>Awake</c>, AND WHY A PREFIX. The colours have to be captured BEFORE
    /// the first <c>Update</c>, or what gets cached as "the base Sky Whale
    /// material" is whatever hue the strobe had reached. <c>Awake</c> is where the
    /// behaviour first resolves <c>sharedMaterial</c> and is guaranteed to run
    /// before any <c>Update</c> or <c>OnEnable</c> on that instance, so a prefix
    /// there is the only place the pristine asset is still on screen. Disabling the
    /// component in the same prefix stops <c>OnEnable</c> firing at all, which is
    /// what takes the audio coroutine with it.
    ///
    /// TIGHTLY SCOPED BY CONSTRUCTION. The patch is on <c>DiscoWhale.Awake</c>, so
    /// it cannot run for anything that is not a whale, and every object it touches
    /// is reached from <c>__instance</c>'s own hierarchy. The one thing that is
    /// global is the material restore - and that is unavoidable, because the bug
    /// being undone is itself a write to a shared asset.
    /// </summary>
    [HarmonyPatch(typeof(DiscoWhale), "Awake")]
    internal static class SkyWhaleUndisco_Patch
    {
        /// <summary>
        /// The two shader properties <c>DiscoWhale.Update</c> writes, resolved once.
        /// <c>_Color</c> is the diffuse tint and <c>_IllumTint</c> the emissive one;
        /// both names are read straight off the decompiled behaviour rather than
        /// guessed, because a wrong id here restores nothing and reports nothing.
        /// </summary>
        private static readonly int DiffuseId = Shader.PropertyToID("_Color");
        private static readonly int EmissiveId = Shader.PropertyToID("_IllumTint");

        /// <summary>
        /// The pristine colours of each whale material, keyed on the shared asset's
        /// instance id.
        ///
        /// CAPTURED ONCE AND RE-APPLIED FOREVER. Once is what makes it pristine -
        /// the first whale to Awake sees the asset before anything has written it.
        /// Forever is what makes it robust: if some path this patch does not cover
        /// ever did strobe it, the next whale to spawn puts it back rather than
        /// baking the damage in.
        /// </summary>
        private static readonly Dictionary<int, Color[]> BaseColours = new Dictionary<int, Color[]>();

        /// <summary>
        /// The behaviour's own serialized renderer reference, resolved once.
        /// Private and <c>[SerializeField]</c>, so it needs reflection - but it is
        /// the RIGHT renderer (the one the strobe writes through) rather than the
        /// first one a hierarchy search happens to find.
        /// </summary>
        private static readonly FieldInfo RendererField =
            AccessTools.Field(typeof(DiscoWhale), "_renderer");

        private static bool _reported;

        [HarmonyPrefix]
        public static bool Awake_Prefix(DiscoWhale __instance)
        {
            if (__instance == null)
            {
                return true;
            }

            try
            {
                RestoreMaterial(__instance);
                KillTheLights(__instance);
                KillTheFireworks(__instance);
                KillTheStrayTree(__instance);

                // Disabling in Awake is what stops OnEnable and Update ever
                // running - and therefore stops both the strobe and the
                // client-local call coroutine. Returning false additionally skips
                // the original Awake, so the behaviour never even caches the shared
                // material it would have written.
                __instance.enabled = false;

                if (!_reported)
                {
                    _reported = true;
                    Debug.Log("[WAReborn] sky whale un-disco'd: hue strobe, 200 m point light, "
                        + "fireworks and the stray tree-trunk Cube are off, and the base "
                        + "Sky Whale material colours are restored. Its call now comes from "
                        + "the server's BigCall entity instead of a client-local timer.");
                }
                return false;
            }
            catch (System.Exception ex)
            {
                // FAIL OPEN, LOUDLY. A disco whale is silly; a whale that throws
                // out of Awake is an entity that never finishes loading, and this
                // patch is cosmetic. Let the original run.
                Debug.LogError("[WAReborn] sky whale un-disco failed, leaving the prefab as "
                    + "shipped: " + ex);
                return true;
            }
        }

        /// <summary>
        /// Puts the shared material's two tints back. See <see cref="BaseColours"/>
        /// for why the first sighting is the one that counts.
        /// </summary>
        private static void RestoreMaterial(DiscoWhale instance)
        {
            Renderer renderer = RendererField != null
                ? RendererField.GetValue(instance) as Renderer
                : null;
            if (renderer == null)
            {
                renderer = instance.GetComponentInChildren<Renderer>(true);
            }
            if (renderer == null)
            {
                return;
            }

            Material material = renderer.sharedMaterial;
            if (material == null)
            {
                return;
            }

            int key = material.GetInstanceID();
            Color[] baseColours;
            if (!BaseColours.TryGetValue(key, out baseColours))
            {
                baseColours = new[]
                {
                    material.HasProperty(DiffuseId) ? material.GetColor(DiffuseId) : Color.white,
                    material.HasProperty(EmissiveId) ? material.GetColor(EmissiveId) : Color.black,
                };
                BaseColours[key] = baseColours;
                return; // nothing has written it yet; there is nothing to restore
            }

            if (material.HasProperty(DiffuseId)) material.SetColor(DiffuseId, baseColours[0]);
            if (material.HasProperty(EmissiveId)) material.SetColor(EmissiveId, baseColours[1]);
        }

        /// <summary>
        /// Every light in the whale's hierarchy, off.
        ///
        /// By COMPONENT TYPE rather than by the child's name: the disco light is a
        /// child called "Light", but a name match would silently do nothing if the
        /// object were ever renamed, and there is no light anywhere on this animal
        /// that should be on. The shipped one is a point light with a 200 m range,
        /// hard shadows and a culling mask of everything - at night, in a world lit
        /// for atmosphere, a single one of those recolours the sky.
        /// </summary>
        private static void KillTheLights(DiscoWhale instance)
        {
            Light[] lights = instance.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;
                lights[i].enabled = false;
                // The GameObject too: a light is cheap to leave enabled-false, but
                // the object may carry a flare or halo that is not a Light
                // component, and nothing else lives on this child.
                if (lights[i].gameObject != instance.gameObject)
                {
                    lights[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Every particle system in the whale's hierarchy, stopped, cleared and
        /// switched off.
        ///
        /// STOPPED AND CLEARED, not merely disabled: the root emitter is
        /// <c>playOnAwake</c>, so by the time a later frame disabled it there would
        /// already be live particles in the world with nothing left to update them.
        /// A child that carries one is deactivated outright; the ROOT one cannot be,
        /// because deactivating the root would take the animal with it, so its
        /// emission module and its renderer are shut individually.
        /// </summary>
        private static void KillTheFireworks(DiscoWhale instance)
        {
            ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem system = systems[i];
                if (system == null) continue;

                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Clear(true);

                if (system.gameObject != instance.gameObject)
                {
                    system.gameObject.SetActive(false);
                    continue;
                }

                // ParticleSystem is a Component, not a Behaviour, in this Unity -
                // there is no `enabled` to clear. Shutting the EMISSION module and
                // the renderer is the equivalent, and the renderer is what actually
                // costs anything.
                ParticleSystem.EmissionModule emission = system.emission;
                emission.enabled = false;
                ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null) renderer.enabled = false;
            }
        }

        /// <summary>
        /// The stray <c>Cube</c>, gone.
        ///
        /// It is a direct child of the root carrying a MeshFilter pointed at
        /// <c>tree_e_section_9_LOD0</c> - a 4.99 x 5.40 x 2.28 m section of tree
        /// trunk - a MeshRenderer in <c>Default-Material</c>, and a BoxCollider, all
        /// at the whale's own origin. It is somebody's forgotten test object.
        ///
        /// Matched by NAME and only among the root's DIRECT children, which is the
        /// tightest thing that identifies it: the whale's real geometry lives under
        /// <c>geometry</c> and its skeleton under <c>SHJntGrp</c>, so a direct child
        /// called "Cube" is unambiguous, and a hierarchy-wide name search could
        /// reach into a rig somebody later renames.
        /// </summary>
        private static void KillTheStrayTree(DiscoWhale instance)
        {
            Transform root = instance.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null && child.name == "Cube")
                {
                    child.gameObject.SetActive(false);
                }
            }
        }
    }
}
