using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// One part MOUNTED on a hull, reduced to what a schematic of the ship needs:
    /// where it sits on the hull, and what KIND of thing it is.
    ///
    /// The coordinates are hull-local world METRES, in the same frame - and at the
    /// same scale - as <see cref="ShipMapSilhouette"/> and <see cref="ShipMapProfile"/>,
    /// so a mark can be drawn straight onto either view with no further arithmetic.
    /// That is not an assumption: a mount offset is the client's own
    /// <c>ship.transform.InverseTransformPoint(...)</c> in Unity metres
    /// (<see cref="PartMount.ShipLocalOffset"/>), and the hull's local frame reaches
    /// the world unrotated with every mesh vertex placed at <c>pos * ShipScale</c>
    /// rather than by scaling the transform (acs/MeshGenerator,
    /// acs/CustomShipFrameVisualizer) - so hull-local metres and scaled plan units
    /// are one frame.
    ///
    /// NO IDENTITY. A mark carries no owner, no part entity id and no uid: it is a
    /// helm at a place on a ship, which is a fact about the ship.
    /// </summary>
    public readonly record struct ShipPartMark(
        string Kind,
        string Title,
        double X,
        double Y,
        double Z);

    /// <summary>
    /// The pure classifier that turns a mounted part's catalogue strings into the
    /// COARSE kind a drawing can render: a schematic wants six or seven symbols, not
    /// the catalogue's thirty-odd rows, and it wants them stable when the catalogue
    /// grows.
    ///
    /// Keyed on the schematic id first because that is the part's OWN identity (the
    /// 1120 itemType, per <c>LoosePartCatalogue</c>), then on the prefab, and only
    /// then on the attachment type - which is the WEAKEST signal of the three, since
    /// nearly everything mounts on "deck".
    ///
    /// An unrecognised part is <see cref="Other"/>, never a guess: a schematic that
    /// drew an unknown part as an engine would be inventing a ship.
    /// </summary>
    public static class ShipPartKinds
    {
        public const string Helm = "helm";
        public const string Sail = "sail";
        public const string Engine = "engine";
        public const string Wing = "wing";
        public const string Lamp = "lamp";
        public const string Core = "core";
        public const string Deck = "deck";
        public const string Other = "part";

        /// <summary>
        /// The kinds a drawer must be able to render, in the order a legend should
        /// list them. Published so the console cannot fall out of step with this
        /// list by hard-coding its own copy.
        /// </summary>
        public static IReadOnlyList<string> All { get; } = new[]
        {
            Helm, Sail, Engine, Wing, Lamp, Core, Deck, Other,
        };

        /// <summary>
        /// Classify one mounted part. Total: every argument may be null or empty,
        /// and the answer is then <see cref="Other"/>.
        /// </summary>
        public static string Classify(string? schematicId, string? prefabName, string? attachmentType)
        {
            string schematic = Lower(schematicId);
            string prefab = Lower(prefabName);
            string attachment = Lower(attachmentType);

            if (schematic == "helm" || prefab.StartsWith("helm", StringComparison.Ordinal)) return Helm;
            if (schematic == "sail" || prefab.StartsWith("sail", StringComparison.Ordinal)) return Sail;
            if (schematic == "deck" || prefab.StartsWith("deck", StringComparison.Ordinal)) return Deck;

            // The sky cores: the main atlas core and its eight modules. Matched on
            // the schematic prefix rather than on the "coreModule" attachment,
            // because the MAIN core mounts on "deck" like everything else.
            if (schematic.StartsWith("skycore", StringComparison.Ordinal)
                || schematic == "atlasskycore"
                || attachment == "coremodule"
                || prefab.StartsWith("core", StringComparison.Ordinal)) return Core;

            if (attachment == "engine" || schematic.Contains("engine", StringComparison.Ordinal)
                || prefab.Contains("engine", StringComparison.Ordinal)) return Engine;
            if (attachment == "wing" || schematic.Contains("wing", StringComparison.Ordinal)
                || prefab.Contains("wing", StringComparison.Ordinal)) return Wing;

            if (schematic.Contains("lamp", StringComparison.Ordinal)
                || prefab.Contains("lamp", StringComparison.Ordinal)) return Lamp;

            return Other;
        }

        /// <summary>
        /// The human words for a kind, for a legend that must not print an
        /// identifier at a reader. Unknown kinds come back capitalised rather than
        /// blank, so a kind added here and not there still reads as something.
        /// </summary>
        public static string Words(string? kind) => Lower(kind) switch
        {
            Helm => "Helm",
            Sail => "Sail",
            Engine => "Engine",
            Wing => "Wing",
            Lamp => "Lamp",
            Core => "Sky core",
            Deck => "Deck piece",
            _ => "Other part",
        };

        private static string Lower(string? value) =>
            string.IsNullOrEmpty(value) ? string.Empty : value!.ToLowerInvariant();
    }
}
