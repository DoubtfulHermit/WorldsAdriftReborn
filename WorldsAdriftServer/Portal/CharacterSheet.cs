using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;

namespace WorldsAdriftServer.Portal
{
    /// <summary>One item type on a character's sheet, and how many they hold.</summary>
    internal sealed record SheetTally(string Name, int Count);

    /// <summary>
    /// What a character knows: the two knowledge totals, what they have learned,
    /// where they got it and what they have scanned.
    ///
    /// Null on a <see cref="CharacterSheet"/> means the character has never had
    /// progression SAVED - a brand new one, or one whose row is unreadable. That
    /// is not the same as "knows nothing", and the page says so differently.
    /// </summary>
    internal sealed record SheetKnowledge(
        int Knowledge,
        int LifetimeKnowledge,
        int Spent,
        IReadOnlyList<string> Schematics,
        IReadOnlyList<SheetTally> NodeUses,
        int NodeUsesTotal,
        int Scans);

    /// <summary>
    /// A character's inventory, summarised. Deliberately a SUMMARY: the portal is
    /// not a second inventory screen, and reproducing the grid would mean a second
    /// renderer of a layout the game already draws - the mistake the emblem
    /// preview exists to avoid.
    /// </summary>
    internal sealed record SheetInventory(
        int Width,
        int Height,
        int Stacks,
        int Units,
        int Worn,
        int Stashed,
        IReadOnlyList<SheetTally> Top);

    /// <summary>
    /// Where the server last saw a character, in metres, and which island's
    /// terrain that is.
    /// </summary>
    /// <param name="Place">
    /// The island's display name, or "open sky" - <c>IslandLocation.Name</c>'s own
    /// words, not a second vocabulary.
    /// </param>
    internal sealed record SheetPosition(
        double MetresX,
        double MetresY,
        double MetresZ,
        string Place,
        bool OnKnownTerrain,
        DateTimeOffset SeenAt);

    /// <summary>Everything the portal shows about one character.</summary>
    internal sealed record CharacterSheet(
        Guid Uid,
        string Name,
        int SlotIndex,
        DateTimeOffset CreatedAt,
        SheetKnowledge? Knowledge,
        SheetInventory? Inventory,
        SheetPosition? Position);

    /// <summary>
    /// Turns the three per-character rows into the sheet the portal draws.
    ///
    /// PURE, and it re-models nothing. Progression and inventory are opaque JSON
    /// columns whose shape belongs to the game server, so they are read back
    /// through the game server's OWN readers - <see cref="ProgressionSnapshot"/>
    /// and <see cref="InventorySnapshot"/> - rather than parsed here. A second
    /// parser of a payload nobody else validates is a parser that silently
    /// disagrees the day the shape moves, and both of those readers already return
    /// null on anything they do not recognise, which is exactly the answer this
    /// page wants: show nothing rather than show a guess.
    ///
    /// WHERE the character is comes in as a delegate rather than a call to
    /// <c>IslandLocationPolicy</c>, so this module can be asserted without loading
    /// the preserved world catalogue. The handler binds the real locator.
    /// </summary>
    internal static class CharacterSheetPolicy
    {
        /// <summary>
        /// How many rows the two "top of the list" tables show.
        ///
        /// A cap rather than the whole list, and it applies to the tallies only:
        /// an inventory can hold hundreds of stacks and a mining character can
        /// have touched hundreds of nodes, and a page that lists all of them is a
        /// page nobody scrolls. The SCHEMATICS list is NOT capped - what a player
        /// has learned is the progression they came to look at, and a truncated
        /// one would be worse than none.
        /// </summary>
        internal const int TallyRows = 8;

        /// <summary>
        /// Answers "which island is this?" for a stored position. Metres, because
        /// the fixed-point encoding is the storage layer's business and the
        /// locator's caller should not have to know about Q52.12.
        /// </summary>
        internal delegate (string Place, bool OnKnownTerrain) Locator(long x, long y, long z);

        /// <summary>A locator for a server with no world data - everything is open sky.</summary>
        internal static readonly Locator NoWorld = static (_, _, _) => ("open sky", false);

        /// <summary>
        /// Builds one character's sheet. Every input except the character itself
        /// may be null, and each null degrades that one panel rather than the
        /// sheet: a player whose inventory row is missing still sees their
        /// knowledge.
        /// </summary>
        internal static CharacterSheet Build(
            CharacterRecord character,
            string? progressionJson,
            string? inventoryJson,
            PositionRecord? position,
            Locator locate)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (locate == null) throw new ArgumentNullException(nameof(locate));

            return new CharacterSheet(
                character.CharacterUid,
                character.Name,
                character.SlotIndex,
                character.CreatedAt,
                KnowledgeFrom(progressionJson),
                InventoryFrom(inventoryJson),
                PositionFrom(position, locate));
        }

        /// <summary>
        /// The knowledge panel, or null when there is no readable progression.
        ///
        /// <c>Spent</c> is derived rather than stored because nothing stores it:
        /// lifetime knowledge only ever grows and current knowledge is what is
        /// left after learning, so the difference is what the character has spent.
        /// Clamped at zero - an operator hand-editing the row could make lifetime
        /// smaller than current, and a negative "spent" would read as a bug in the
        /// game rather than in the row.
        /// </summary>
        private static SheetKnowledge? KnowledgeFrom(string? json)
        {
            ProgressionState? state = ProgressionSnapshot.Read(json);
            if (state == null) return null;

            List<SheetTally> uses = new List<SheetTally>();
            int total = 0;

            foreach (KeyValuePair<string, int> pair in state.NodeUses)
            {
                uses.Add(new SheetTally(pair.Key, pair.Value));
                total += pair.Value;
            }

            uses.Sort(CompareTallies);
            if (uses.Count > TallyRows) uses.RemoveRange(TallyRows, uses.Count - TallyRows);

            List<string> schematics = new List<string>(state.LearnedSchematics);
            schematics.Sort(StringComparer.Ordinal);

            return new SheetKnowledge(
                state.Knowledge,
                state.LifetimeKnowledge,
                Math.Max(0, state.LifetimeKnowledge - state.Knowledge),
                schematics,
                uses,
                total,
                state.AlreadyScanned.Count);
        }

        /// <summary>
        /// The inventory summary, or null when there is no readable inventory.
        ///
        /// STACKS and UNITS are counted separately because they answer different
        /// questions and a single "items" number would silently be one of them:
        /// twenty stacks of five is twenty rows in the grid and a hundred things
        /// in the world.
        /// </summary>
        private static SheetInventory? InventoryFrom(string? json)
        {
            InventoryModel? model = InventorySnapshot.Read(json);
            if (model == null) return null;

            Dictionary<string, int> byType = new Dictionary<string, int>(StringComparer.Ordinal);
            int units = 0;
            int worn = 0;
            int stashed = 0;

            foreach (InventoryItem item in model.Items)
            {
                int amount = Math.Max(0, item.Amount);
                units += amount;

                if (item.IsWorn) worn++;
                if (item.IsStashed) stashed++;

                byType.TryGetValue(item.ItemTypeId, out int held);
                byType[item.ItemTypeId] = held + amount;
            }

            List<SheetTally> top = new List<SheetTally>();
            foreach (KeyValuePair<string, int> pair in byType)
            {
                top.Add(new SheetTally(pair.Key, pair.Value));
            }

            top.Sort(CompareTallies);
            if (top.Count > TallyRows) top.RemoveRange(TallyRows, top.Count - TallyRows);

            return new SheetInventory(
                model.Width, model.Height, model.Items.Count, units, worn, stashed, top);
        }

        /// <summary>
        /// The position panel, or null when the character has never been saved
        /// anywhere - which is the normal answer for one who has not entered the
        /// world yet, not an error.
        /// </summary>
        private static SheetPosition? PositionFrom(PositionRecord? position, Locator locate)
        {
            if (position == null) return null;

            (string place, bool known) = locate(position.X, position.Y, position.Z);

            // The stored units are Q52.12 - 4096 to the metre - and the conversion
            // is the storage encoding's own, not a constant restated here.
            const double unitsPerMetre =
                WorldsAdriftRebornGameServer.Multiplayer.FixedPointPosition.UnitsPerMetre;

            return new SheetPosition(
                position.X / unitsPerMetre,
                position.Y / unitsPerMetre,
                position.Z / unitsPerMetre,
                place,
                known,
                position.UpdatedAt);
        }

        /// <summary>
        /// Biggest first, then by name. The name tie-break is not decoration: two
        /// item types with the same count would otherwise swap places between
        /// page loads on the same data, and a table that reorders itself for no
        /// reason reads as a page showing something live when it is not.
        /// </summary>
        private static int CompareTallies(SheetTally a, SheetTally b)
        {
            int byCount = b.Count.CompareTo(a.Count);
            return byCount != 0 ? byCount : string.CompareOrdinal(a.Name, b.Name);
        }
    }
}
