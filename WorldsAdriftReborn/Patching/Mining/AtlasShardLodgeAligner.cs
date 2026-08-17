using System;
using System.Collections;
using Assets.Visualizers;
using Bossa.Travellers.Interact;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Mining
{
    /// <summary>
    /// Puts a lodged ATLAS SHARD where it belongs: in its host deposit's core.
    ///
    /// WHY THIS EXISTS. In retail the shard's world placement was done by
    /// <c>MetalDepositAtlasVisualiser_fsim</c>:
    ///
    ///     AttachSlot(coreVisualiser.Visuals.ScrapSlots[_state.SlotId]);   // :78
    ///     private void AttachSlot(MetalScrapSlot slot)                    // :130-133
    ///         =&gt; base.transform.AlignTo(slot.transform);
    ///     base.gameObject.SetLayerRecursively(Layers.Interactive);        // :92
    ///     SendLocation();                                                 // :93
    ///
    /// and that class is <c>[WorkerType(WorkerPlatform.UnityWorker)]</c> (:9), so
    /// <c>MetalDepositAtlasPreprocessor</c> only ever puts it on the WORKER build. The
    /// CLIENT gets <c>MetalDepositAtlasVisualiser_client</c>, whose whole init is
    /// "find the view, ReloadModel()" - it never aligns anything and never sets the
    /// layer. WAReborn has no UnityWorker, so on a stock client:
    ///
    ///   * the shard renders at whatever 190602 the server wrote - which is why it
    ///     showed up as a free-floating crystal near the rock instead of embedded in
    ///     its core; and
    ///   * it keeps the shard prefab's authored layer, so if that layer is not in
    ///     <c>Layers.Interactables</c> the "Pick Up" raycast in
    ///     <c>PlayerLookingAt</c> can never hit it and no prompt can ever appear.
    ///
    /// The slot transform is a PREFAB fact - <c>MetalDepositCoreVisuals.ScrapSlots</c>
    /// is a serialized array on the core prefab the client imports at runtime - so no
    /// server position can stand in for it. This component is therefore the retail
    /// worker behaviour RELOCATED to the only worker this revival has, not an
    /// invention: same source (ScrapSlots[slotId]), same layer, same "let go when the
    /// core explodes" reaction.
    ///
    /// WHAT IT MOVES. The retail code aligned the ENTITY ROOT and then pushed the
    /// result back over the network. This client cannot do the second half (it is not
    /// authoritative over the shard), and moving the root would fight whichever
    /// TransformState behaviour the shard prefab authored. So it parents the shard's
    /// VIEW - a child of the entity root - to the slot and gives it the SAME local
    /// offset it had under the root. The result is pixel-identical to "root aligned to
    /// slot" while leaving the entity's own transform untouched for the network layer.
    ///
    /// It is deliberately paranoid: every step is null-guarded and the whole coroutine
    /// is wrapped, because a throw here would take a shard - and possibly a rock - out
    /// of the world silently.
    /// </summary>
    internal class AtlasShardLodgeAligner : MonoBehaviour
    {
        /// <summary>How long to wait for the host core to stream in before giving up.</summary>
        private const float CoreWaitTimeoutSeconds = 120f;

        private Transform _view;
        private Transform _originalParent;
        private Vector3 _originalLocalPosition;
        private Quaternion _originalLocalRotation;
        private bool _captured;

        private MetalDepositCoreVisuals _core;
        private Action _onCoreExploded;
        private Coroutine _routine;
        private Transform _slotTransform;
        private bool _following;
        private Renderer[] _renderers;
        private InteractiveObjectVisualizer _interactVis;
        private static readonly System.Reflection.FieldInfo InteractiveField =
            HarmonyLib.AccessTools.Field(typeof(InteractiveObjectVisualizer), "_interactive");
        private bool _hidden;

        private long _shardEntityId;
        private long _rockCoreId;
        private int _slotId;

        /// <summary>
        /// Starts the alignment for one shard. <paramref name="view"/> is the shard's
        /// <c>MetalDepositAtlasView</c> transform (the child that carries the imported
        /// crystal model and its collider).
        /// </summary>
        internal void Begin(Transform view, long shardEntityId, long rockCoreId, int slotId)
        {
            _view = view;
            _shardEntityId = shardEntityId;
            _rockCoreId = rockCoreId;
            _slotId = slotId;

            if (_view == null)
            {
                Debug.LogWarning("[WAR][atlas] shard " + _shardEntityId
                    + " has no MetalDepositAtlasView; cannot lodge it in a core.");
                return;
            }

            // Capture the view's pose relative to the ENTITY ROOT before touching it.
            // Re-applying exactly this under the slot reproduces the retail
            // "transform.AlignTo(slot.transform)" on the root.
            _originalParent = _view.parent;
            _originalLocalPosition = _view.localPosition;
            _originalLocalRotation = _view.localRotation;
            _captured = true;

            // The interaction raycast (PlayerLookingAt, mask Layers.Interactables) is the
            // only thing that can raise a PickUp prompt, and the retail worker is what
            // put the shard on a layer that mask covers. Do it here, unconditionally -
            // it is a no-op if the prefab already authored the right layer.
            try
            {
                gameObject.SetLayerRecursively(Layers.Interactive);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WAR][atlas] shard " + _shardEntityId
                    + ": could not set the Interactive layer: " + ex.Message);
            }

            _routine = StartCoroutine(AlignRoutine());
        }

        private IEnumerator AlignRoutine()
        {
            if (_rockCoreId <= 0)
            {
                Debug.LogWarning("[WAR][atlas] shard " + _shardEntityId
                    + " has an invalid rockCoreId (" + _rockCoreId
                    + "); it will stay at its server position instead of in a core.");
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + CoreWaitTimeoutSeconds;

            // 1. Wait for the host deposit entity and its core visualiser's imported
            //    visuals - the same two waits the retail fsim did (FetchEntity.WaitFor
            //    then Job.WaitUntilRoutine(() => coreVisualiser.Visuals)), but bounded so
            //    a shard whose deposit never renders cannot leak a coroutine forever.
            MetalDepositCoreVisuals visuals = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                visuals = TryResolveCoreVisuals();
                if (visuals != null)
                {
                    break;
                }
                yield return null;
            }

            if (visuals == null)
            {
                Debug.LogWarning("[WAR][atlas] shard " + _shardEntityId + ": host core entity "
                    + _rockCoreId + " never produced MetalDepositCoreVisuals within "
                    + CoreWaitTimeoutSeconds + "s. The shard stays at its server position. "
                    + "This almost always means the DEPOSIT itself never rendered - check the "
                    + "[WAR][deposit] lines above.");
                yield break;
            }

            // 2. Align to the authored slot, exactly as MetalDepositAtlasVisualiser_fsim
            //    .AttachSlot did. ScrapSlots is indexed UNGUARDED in retail; guard it,
            //    because a server slotId beyond the prefab's array would be an
            //    IndexOutOfRangeException in the middle of a coroutine.
            MetalScrapSlot[] slots = visuals.ScrapSlots;
            if (slots == null || slots.Length == 0)
            {
                Debug.LogWarning("[WAR][atlas] shard " + _shardEntityId + ": core "
                    + _rockCoreId + " has no ScrapSlots; leaving the shard where it is.");
                yield break;
            }

            int slot = _slotId;
            if (slot < 0 || slot >= slots.Length)
            {
                Debug.LogWarning("[WAR][atlas] shard " + _shardEntityId + ": slotId " + slot
                    + " is outside the core's " + slots.Length
                    + " ScrapSlots; falling back to slot 0.");
                slot = 0;
            }

            MetalScrapSlot target = slots[slot];
            if (target == null || target.transform == null || _view == null)
            {
                Debug.LogWarning("[WAR][atlas] shard " + _shardEntityId + ": slot " + slot
                    + " on core " + _rockCoreId + " is null; leaving the shard where it is.");
                yield break;
            }

            // FOLLOW the slot's pose - do NOT reparent. Reparenting the view (and its
            // collider) into the CORE's hierarchy broke pickup entirely: the interact
            // raycast resolves the hit collider upward to the entity that owns it, and
            // under the core's transform that is the DEPOSIT, not the shard - so the
            // shard's InteractiveObjectVisualizer was never consulted, no prompt could
            // appear, and E produced no 1211 (VERIFIED live: server pushed
            // available=true to the peer, zero PickUp attempts ever arrived). Keeping
            // the view under the shard's own entity root preserves raycast->shard
            // resolution; LateUpdate keeps the pose glued to the slot.
            _slotTransform = target.transform;
            _following = true;
            _renderers = _view != null ? _view.GetComponentsInChildren<Renderer>(true) : null;
            _interactVis = GetComponent<InteractiveObjectVisualizer>();
            FollowSlot();
            UpdateVisibility();

            Debug.Log("[WAR][atlas] shard " + _shardEntityId + " lodged in core " + _rockCoreId
                + " slot " + slot + " (" + slots.Length + " slot(s) on this variant, follow-pose).");

            // 3. When the core blows, the shard is no longer held by the rock - retail let
            //    its rigidbody go (MetalDepositAtlasVisualiser_fsim.OnCoreExploded). This
            //    client is not authoritative over the shard, so it cannot simulate a fall;
            //    the faithful equivalent is to hand the shard back to its own entity
            //    transform, i.e. to the position the server says it is at.
            _core = visuals;
            _onCoreExploded = OnCoreExploded;
            _core.Exploded += _onCoreExploded;
        }

        private MetalDepositCoreVisuals TryResolveCoreVisuals()
        {
            try
            {
                Improbable.Unity.Entity.IEntityObject entity =
                    Improbable.Unity.Core.SpatialOS.Universe.Get(new Improbable.EntityId(_rockCoreId));
                if (entity == null || entity.UnderlyingGameObject == null)
                {
                    return null;
                }

                MetalDepositCoreVisualiser coreVisualiser =
                    entity.UnderlyingGameObject.GetComponent<MetalDepositCoreVisualiser>();
                return coreVisualiser == null ? null : coreVisualiser.Visuals;
            }
            catch (Exception)
            {
                // The universe lookup throws while the entity is mid-checkout; retry.
                return null;
            }
        }

        /// <summary>
        /// Copies the slot's pose onto the view - the world pose parenting would have
        /// produced (slot * originalLocal), without the parenting. Runs every LateUpdate
        /// while lodged so the crystal stays glued even if the core's visuals settle late.
        /// </summary>
        private void FollowSlot()
        {
            if (_view == null || _slotTransform == null)
            {
                return;
            }
            _view.position = _slotTransform.TransformPoint(_originalLocalPosition);
            _view.rotation = _slotTransform.rotation * _originalLocalRotation;
        }

        private void LateUpdate()
        {
            if (_following)
            {
                if (_slotTransform == null)
                {
                    // The core's visuals were destroyed under us (explosion path raced the
                    // event) - fall back to the entity's own pose.
                    Detach();
                    return;
                }

                // COLLECTED detection: when a player takes the shard, the server SINKS the
                // shard entity (moves its 190602 far away) - but this follow was gluing the
                // crystal to the slot regardless, leaving a GHOST shard visibly stuck in the
                // rock after collection. If the entity root has moved well away from the
                // slot, the shard is gone - stop following and snap the view back to the
                // entity's own (sunk, out-of-sight) pose.
                Transform root = _view != null ? _view.parent : null;
                if (root != null
                    && (root.position - _slotTransform.position).sqrMagnitude > 400f)
                {
                    Debug.Log("[WAR][atlas] shard " + _shardEntityId
                        + " entity moved far from its slot (collected/sunk) - releasing the view.");
                    Detach();
                    return;
                }

                FollowSlot();
                UpdateVisibility();
            }
        }

        /// <summary>
        /// Retail hides the shard inside the closed rock: the crystal is only seen once
        /// the crust is broken open. Our slot pose can poke through an intact shell, so
        /// while LODGED the crystal renders only when the server says it is takeable
        /// (1210 available flips true on crust-break exposure). Released/collected
        /// shards are always visible. Reflection is null-safe: if anything is missing,
        /// the shard stays VISIBLE - a poking crystal is a cosmetic bug, an invisible
        /// grabbable one is a gameplay bug.
        /// </summary>
        private void UpdateVisibility()
        {
            if (_renderers == null || _renderers.Length == 0)
            {
                return;
            }

            bool visible = true;
            try
            {
                if (_following && _interactVis != null && InteractiveField != null)
                {
                    var reader = InteractiveField.GetValue(_interactVis) as InteractiveStateReader;
                    if (reader != null)
                    {
                        visible = reader.Available;
                    }
                }
            }
            catch (Exception)
            {
                visible = true;
            }

            if (visible == !_hidden)
            {
                return;
            }
            _hidden = !visible;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].enabled = visible;
                }
            }
        }

        private void OnCoreExploded()
        {
            Debug.Log("[WAR][atlas] shard " + _shardEntityId + ": host core " + _rockCoreId
                + " exploded - releasing the shard back to its own transform.");
            Detach();
        }

        /// <summary>
        /// Puts the view back under the entity root with the pose it started with. Safe
        /// to call more than once, and it MUST run before the shard's GameObject is
        /// destroyed: the view is currently parented to the CORE's hierarchy, so leaving
        /// it there would orphan it into the rock when the shard entity goes away.
        /// </summary>
        private void Detach()
        {
            _following = false;
            _slotTransform = null;
            // A freed (or sunk) shard must never stay hidden.
            UpdateVisibility();

            if (_core != null && _onCoreExploded != null)
            {
                _core.Exploded -= _onCoreExploded;
                _onCoreExploded = null;
                _core = null;
            }

            if (!_captured || _view == null)
            {
                return;
            }

            // The view never left the entity root (follow-pose, not reparent) - just
            // restore its authored local pose so the freed shard sits at the entity's
            // own served position again.
            _view.localPosition = _originalLocalPosition;
            _view.localRotation = _originalLocalRotation;
        }

        private void OnDisable()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            Detach();
        }

        private void OnDestroy()
        {
            Detach();
        }
    }
}
