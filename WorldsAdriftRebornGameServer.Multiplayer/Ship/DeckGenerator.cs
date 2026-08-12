using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// A small engine-free float 3-vector for the deck derivation. Deliberately NOT
    /// Unity's <c>Vector3</c> nor Improbable's <c>Vector3f</c> - the Multiplayer
    /// assembly must stay free of both - but its axes and float precision match them
    /// exactly (x,y,z; single precision), so a value computed here serialises to the
    /// client's 1518 <c>Vector3f</c> without conversion beyond a struct copy.
    /// </summary>
    public readonly struct ShipVector3 : IEquatable<ShipVector3>
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public ShipVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static ShipVector3 operator +(ShipVector3 a, ShipVector3 b)
            => new ShipVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static ShipVector3 operator -(ShipVector3 a, ShipVector3 b)
            => new ShipVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static ShipVector3 operator *(ShipVector3 v, float s)
            => new ShipVector3(v.X * s, v.Y * s, v.Z * s);

        public static ShipVector3 operator *(float s, ShipVector3 v) => v * s;

        public static ShipVector3 operator /(ShipVector3 v, float s)
            => new ShipVector3(v.X / s, v.Y / s, v.Z / s);

        /// <summary>Squared magnitude, matching Unity's <c>Vector3.sqrMagnitude</c>.</summary>
        public float SqrMagnitude => X * X + Y * Y + Z * Z;

        /// <summary>Magnitude, matching Unity's <c>Vector3.magnitude</c>.</summary>
        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);

        /// <summary>Cross product, matching Unity's <c>Vector3.Cross(a,b)</c> component order.</summary>
        public static ShipVector3 Cross(ShipVector3 a, ShipVector3 b)
            => new ShipVector3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);

        /// <summary>Unclamped lerp <c>(1-t)*a + t*b</c>, matching <c>MathUtils.Lerp(Vector3,...)</c>.</summary>
        public static ShipVector3 Lerp(ShipVector3 a, ShipVector3 b, float t)
            => (1f - t) * a + t * b;

        public bool Equals(ShipVector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object? obj) => obj is ShipVector3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>
    /// One derived deck panel: the exact server-side equivalent of the client's
    /// <c>ShipHullPartData.ShipDeck</c> after subdivision. It is the split the client's
    /// original <c>CreateDeck</c> makes (acs/ShipDeckSpawningVisualizer.cs:42-45):
    /// <see cref="HullLocalPositionMetres"/> is <c>deck.Position * 2f</c> (the deck
    /// entity's transform), and <see cref="LocalVertices"/> is <c>deck.Vertices</c>
    /// UNCHANGED - centroid-relative, in raw ShipPlan units, NOT multiplied by two. The
    /// client re-applies scale 2 in <c>MeshGenerator.MakeMesh</c>.
    /// </summary>
    public readonly struct DeckPanel
    {
        public DeckPanel(
            ShipVector3 hullLocalPositionMetres,
            IReadOnlyList<ShipVector3> localVertices,
            int sourceDeckNumber,
            int sourceQuadIndex)
        {
            HullLocalPositionMetres = hullLocalPositionMetres;
            LocalVertices = localVertices;
            SourceDeckNumber = sourceDeckNumber;
            SourceQuadIndex = sourceQuadIndex;
        }

        /// <summary>The deck entity's hull-local position in metres: <c>2 * panelCentroid</c>.</summary>
        public ShipVector3 HullLocalPositionMetres { get; }

        /// <summary>The panel's perimeter loop, centroid-relative, in raw (unscaled) ShipPlan units.</summary>
        public IReadOnlyList<ShipVector3> LocalVertices { get; }

        /// <summary>Diagnostic: which deck number (y) the source quad came from. Not used for geometry.</summary>
        public int SourceDeckNumber { get; }

        /// <summary>Diagnostic: index of the source flat quad in generation order. Not used for geometry.</summary>
        public int SourceQuadIndex { get; }
    }

    /// <summary>
    /// A pure, engine-free, literal fidelity port of the client's
    /// <c>ShipPlan -&gt; ShipHullPartData.Decks</c> derivation
    /// (acs/ShipPlan.cs + acs/ShipHullPartData.cs in the decompile). Given a decoded
    /// <see cref="ShipPlanModel"/> it reproduces, panel-for-panel, the deck surfaces the
    /// client itself builds:
    ///
    ///   1. materialise the hull's graph vertices exactly as <c>ShipPlan.MakeVertices</c>
    ///      (one per unique section/side/level, with the same adjacency flags);
    ///   2. infer connections exactly as <c>ShipPlan.GenerateConnectionsForVertexPair</c>;
    ///   3. generate candidate quads exactly as
    ///      <c>ShipHullPartData.GenerateQuadDatas_Internal</c> (three topology cases);
    ///   4. drop degenerate quads by the same area threshold and keep only the FLAT
    ///      (equal-y within 0.001) candidates - the decks;
    ///   5. centroid-align each flat quad and clip it into lateral x strips exactly as
    ///      <c>SubdivideDeck</c> / <c>ClipX</c>;
    ///   6. emit one <see cref="DeckPanel"/> per clipped polygon of 3+ vertices.
    ///
    /// The client uses <c>float</c> and Unity <c>Vector3</c> throughout; this port uses
    /// <c>float</c> and <see cref="ShipVector3"/> so the same rounding decisions are made.
    /// No Unity or Improbable type is referenced.
    /// </summary>
    public static class DeckGenerator
    {
        private const int LevelsPerSection = 4;

        /// <summary>
        /// The graph vertex, a field-for-field mirror of <c>ShipPlan.ShipVertex</c>
        /// (acs/ShipPlan.cs:10-66). Only the fields the deck derivation reads are kept.
        /// </summary>
        private sealed class Vertex
        {
            public ShipVector3 Pos;
            public bool IsMainVertex;
            public int Idx = -1;
            public int PrevIdx = -1;
            public int NextIdx = -1;
            public int BelowIdx = -1;
            public int AboveIdx = -1;
            public int SideIdx = -1;
            public bool HasAbove;
            public bool HasBelow;
            public bool HasForward;
            public bool HasAstern;
            public int DeckNumber;
            public int LevelNumber;
            public int LayerNumber;
            public int SectionNumber;
            public int SideOfSection;
        }

        /// <summary>Mirror of <c>ShipHullPartData.QuadData</c>: four vertex indices in perimeter order.</summary>
        private readonly struct QuadData
        {
            public readonly int Index0;
            public readonly int Index1;
            public readonly int Index2;
            public readonly int Index3;
            public readonly bool Flip;

            public QuadData(int index0, int index1, int index2, int index3, bool flip)
            {
                Index0 = index0;
                Index1 = index1;
                Index2 = index2;
                Index3 = index3;
                Flip = flip;
            }
        }

        /// <summary>
        /// The centroid-aligned quad handed to subdivision: a mirror of
        /// <c>ShipHullPartData.ShipQuad</c>. <c>Vertices</c> are centroid-relative and
        /// <c>Position</c> is the centroid (both in hull-local raw units).
        /// </summary>
        private sealed class ShipQuad
        {
            public readonly ShipVector3[] Vertices;
            public readonly ShipVector3 Position;

            public ShipQuad(ShipVector3[] vertices)
            {
                var arr = (ShipVector3[])vertices.Clone();
                Position = CentroidAlign(arr);
                Vertices = arr;
            }

            private static ShipVector3 CentroidAlign(ShipVector3[] vs)
            {
                ShipVector3 c = new ShipVector3(0f, 0f, 0f);
                int n = vs.Length;
                for (int i = 0; i < n; i++)
                {
                    c += vs[i];
                }
                c /= n;
                for (int j = 0; j < n; j++)
                {
                    vs[j] -= c;
                }
                return c;
            }
        }

        /// <summary>
        /// Derive the deck panels for a hull design. Deterministic: panel order follows
        /// the source cell order in <paramref name="plan"/>, then quad generation order,
        /// then subdivision order, so a restore from the same hull bytes reproduces the
        /// same panels and the same registration keys.
        ///
        /// Throws <see cref="InvalidOperationException"/> for a structurally broken plan
        /// (an aft-most cell with no Back section); the caller's failure policy then falls
        /// back to the minimum hull. A well-formed plan that simply has no flat surface
        /// returns an empty list (also a fallback trigger for the caller).
        /// </summary>
        public static IReadOnlyList<DeckPanel> Generate(ShipPlanModel plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            List<Vertex> vertices = MakeVertices(plan);
            GenerateConnections(vertices);

            var result = new List<DeckPanel>();
            int quadIndex = 0;
            foreach (QuadData qd in GenerateQuadDatas(vertices))
            {
                if (!IsDeck(vertices[qd.Index0].Pos, vertices[qd.Index1].Pos,
                            vertices[qd.Index2].Pos, vertices[qd.Index3].Pos))
                {
                    continue;
                }

                var quad = new ShipQuad(new[]
                {
                    vertices[qd.Index0].Pos,
                    vertices[qd.Index1].Pos,
                    vertices[qd.Index2].Pos,
                    vertices[qd.Index3].Pos,
                });

                int sourceDeck = vertices[qd.Index0].DeckNumber;
                foreach (List<ShipVector3> hullLocalPolygon in SubdivideDeck(quad))
                {
                    if (hullLocalPolygon.Count < 3)
                    {
                        continue;
                    }

                    ShipVector3 centroid = Centroid(hullLocalPolygon);
                    var local = new ShipVector3[hullLocalPolygon.Count];
                    for (int i = 0; i < hullLocalPolygon.Count; i++)
                    {
                        local[i] = hullLocalPolygon[i] - centroid;
                    }

                    result.Add(new DeckPanel(
                        hullLocalPositionMetres: 2f * centroid,
                        localVertices: local,
                        sourceDeckNumber: sourceDeck,
                        sourceQuadIndex: quadIndex));
                }

                quadIndex++;
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Vertex construction - literal port of ShipPlan.MakeVertices
        // (acs/ShipPlan.cs:139-181) plus neighbour resolution (SetNeighbours,
        // HasNeighbourAbove/Below).
        // ------------------------------------------------------------------

        private static List<Vertex> MakeVertices(ShipPlanModel plan)
        {
            var byCoord = new Dictionary<(int cell, int deck), ShipCellModel>();
            foreach (ShipCellModel cell in plan.Cells)
            {
                // Duplicate cell coordinates are rejected: the client's dictionary keyed
                // by (cellNumber, deckNumber) cannot hold two, so a blob claiming two is
                // malformed and must not silently produce doubled geometry.
                byCoord[(cell.CellNumber, cell.DeckNumber)] = cell;
            }

            bool HasCell(int c, int d) => byCoord.ContainsKey((c, d));

            var list = new List<Vertex>();
            foreach (ShipCellModel cell in plan.Cells)
            {
                int c = cell.CellNumber;
                int d = cell.DeckNumber;

                bool astern = HasCell(c - 1, d);
                bool forward = HasCell(c + 1, d);
                bool above = HasCell(c, d + 1);
                bool below = HasCell(c, d - 1);

                for (int i = 0; i < 2; i++)
                {
                    // i==0 is the Back section, i==1 the Front. The client skips Back
                    // whenever an astern neighbour exists (that boundary is the neighbour's
                    // Front), so Back is only ever materialised for the aft-most cell.
                    if (i == 0 && astern)
                    {
                        continue;
                    }

                    ShipSectionModel section = (i != 0) ? cell.Front : cell.Back
                        ?? throw new InvalidOperationException(
                            $"Cell ({c},{d}) has no astern neighbour and no Back section; hull is malformed.");

                    int sectionNumber = c + i;

                    // HasNeighbourAbove/Below: the direct neighbour, OR a diagonally
                    // staggered cell one deck up/down and one section across in the
                    // direction this section faces (acs/ShipPlan.cs:183-201).
                    int diag = i * 2 - 1;
                    bool hasAboveNeighbour = above || HasCell(c + diag, d + 1);
                    bool hasBelowNeighbour = below || HasCell(c + diag, d - 1);

                    for (int j = 0; j < 2; j++)
                    {
                        for (int k = 0; k < LevelsPerSection; k++)
                        {
                            list.Add(new Vertex
                            {
                                IsMainVertex = (k == 0 || k == LevelsPerSection - 1),
                                Pos = GetCurvePosition(section, sectionNumber, d, k, j),
                                Idx = list.Count,
                                DeckNumber = d,
                                LevelNumber = k,
                                LayerNumber = d * LevelsPerSection + k,
                                SectionNumber = sectionNumber,
                                SideOfSection = j,
                                HasAbove = (k != LevelsPerSection - 1) || hasAboveNeighbour,
                                HasBelow = (k != 0) || hasBelowNeighbour,
                                HasForward = (i == 0) || forward,
                                HasAstern = (i == 1) || astern,
                            });
                        }
                    }
                }
            }

            return list;
        }

        // ------------------------------------------------------------------
        // Section-to-hull coordinate conversion - literal port of
        // ShipSection.GetCurvePosition / GetBaseCurvePosition / GetVertexOffset
        // (acs/ShipSection.cs:188-225).
        // ------------------------------------------------------------------

        private static ShipVector3 GetVertexOffset(int level, int sectionN, int deckN)
        {
            float t = level / 3f;
            return new ShipVector3(
                0f,
                deckN * 1.7f + Lerp1(0f, 1.7f, t),
                (sectionN - 0.5f) * 2f);
        }

        private static ShipVector3 GetBaseCurvePosition(
            ShipSectionModel s, int sectionN, int deckN, int level, int side)
        {
            ShipVector3 a = ToVec(s.Bottom[side]) + GetVertexOffset(0, sectionN, deckN);
            ShipVector3 b = ToVec(s.Top[side]) + GetVertexOffset(3, sectionN, deckN);
            return ShipVector3.Lerp(a, b, level / 3f);
        }

        private static ShipVector3 GetCurvePosition(
            ShipSectionModel s, int sectionN, int deckN, int level, int side)
        {
            float x = 0f;
            int curveIndex = level - 1;
            if (curveIndex >= 0 && curveIndex < 2)
            {
                x = s.Curve[curveIndex, side];
            }
            return GetBaseCurvePosition(s, sectionN, deckN, level, side) + new ShipVector3(x, 0f, 0f);
        }

        // ------------------------------------------------------------------
        // Connections - literal port of ShipPlan.GenerateConnections /
        // GenerateConnectionsForVertexPair (acs/ShipPlan.cs:203-253). O(n^2) over
        // ordered pairs, exactly as the client, so the result is order-independent
        // for geometry.
        // ------------------------------------------------------------------

        private static void GenerateConnections(List<Vertex> vertices)
        {
            int count = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                Vertex v = vertices[i];
                for (int j = i + 1; j < count; j++)
                {
                    GenerateConnectionsForVertexPair(v, vertices[j]);
                }
            }
        }

        private static void GenerateConnectionsForVertexPair(Vertex v0, Vertex v1)
        {
            if (v0.LayerNumber == v1.LayerNumber)
            {
                if (v0.SideOfSection == v1.SideOfSection)
                {
                    if (v0.HasForward && v0.SectionNumber == v1.SectionNumber - 1)
                    {
                        v0.NextIdx = v1.Idx;
                        v1.PrevIdx = v0.Idx;
                    }
                    else if (v0.HasAstern && v0.SectionNumber == v1.SectionNumber + 1)
                    {
                        v0.PrevIdx = v1.Idx;
                        v1.NextIdx = v0.Idx;
                    }
                }
                else if (v0.SectionNumber == v1.SectionNumber)
                {
                    v0.SideIdx = v1.Idx;
                    v1.SideIdx = v0.Idx;
                }
            }
            else if (v0.LayerNumber == v1.LayerNumber - 1)
            {
                if (v0.HasAbove && v0.SectionNumber == v1.SectionNumber && v0.SideOfSection == v1.SideOfSection)
                {
                    v0.AboveIdx = v1.Idx;
                    v1.BelowIdx = v0.Idx;
                }
            }
            else if (v0.LayerNumber == v1.LayerNumber + 1 && v0.HasBelow
                     && v0.SectionNumber == v1.SectionNumber && v0.SideOfSection == v1.SideOfSection)
            {
                v0.BelowIdx = v1.Idx;
                v1.AboveIdx = v0.Idx;
            }
        }

        // ------------------------------------------------------------------
        // Candidate quads - literal port of GenerateQuadDatas_Internal +
        // FilterOutLowAreaQuads (acs/ShipHullPartData.cs:322-370).
        // ------------------------------------------------------------------

        private static IEnumerable<QuadData> GenerateQuadDatas(List<Vertex> vs)
        {
            foreach (QuadData qd in GenerateQuadDatas_Internal(vs))
            {
                if (GetArea(vs[qd.Index0].Pos, vs[qd.Index1].Pos, vs[qd.Index2].Pos, vs[qd.Index3].Pos)
                    > 9.999999747378752E-05)
                {
                    yield return qd;
                }
            }
        }

        private static IEnumerable<QuadData> GenerateQuadDatas_Internal(List<Vertex> vs)
        {
            foreach (Vertex v0 in vs)
            {
                // Case 1: hull end cap between a level and the level above, across both
                // sides. Vertical normally; a deck only if edited flat.
                if (v0.SideOfSection == 0 && v0.AboveIdx != -1 && (v0.NextIdx == -1 || v0.PrevIdx == -1))
                {
                    Vertex v3 = vs[v0.SideIdx];
                    Vertex v7 = vs[v0.AboveIdx];
                    yield return new QuadData(
                        index0: v3.Idx,
                        index1: v0.Idx,
                        index2: v7.Idx,
                        index3: vs[v3.AboveIdx].Idx,
                        flip: v0.NextIdx == -1);
                }

                // Case 2: longitudinal side quad between adjacent sections and adjacent
                // levels, with a special repair for a flat quad across a staggered
                // vertical connection.
                if (v0.NextIdx != -1 && v0.AboveIdx != -1)
                {
                    Vertex v2 = vs[v0.NextIdx];
                    if (v2.AboveIdx != -1)
                    {
                        Vertex v6 = vs[v0.AboveIdx];
                        Vertex v8 = vs[v2.AboveIdx];
                        if (v6.NextIdx == v8.Idx)
                        {
                            yield return new QuadData(v2.Idx, v0.Idx, v6.Idx, v8.Idx, v0.SideOfSection == 0);
                        }
                        else if (v0.SideOfSection == 0
                                 && Approximately(v0.Pos.Y, v2.Pos.Y)
                                 && Approximately(v0.Pos.Y, v6.Pos.Y)
                                 && Approximately(v0.Pos.Y, v8.Pos.Y))
                        {
                            v6 = vs[vs[v6.SideIdx].BelowIdx];
                            yield return new QuadData(
                                index0: v2.Idx,
                                index1: v0.Idx,
                                index2: v6.Idx,
                                index3: vs[vs[v8.SideIdx].BelowIdx].Idx,
                                flip: v0.SideOfSection == 0);
                        }
                    }
                }

                // Case 3: cross-beam quad between adjacent sections, side 0 to side 1.
                // Emitted at level 0 for every connected interval, and wherever either
                // endpoint has nothing above (the exposed top deck). The ordinary
                // per-cell floor/top-deck case.
                if (v0.NextIdx != -1)
                {
                    Vertex v1 = vs[v0.NextIdx];
                    if (v0.SideOfSection == 0 && (v0.LevelNumber == 0 || v0.AboveIdx == -1 || v1.AboveIdx == -1))
                    {
                        Vertex v4 = vs[v0.SideIdx];
                        yield return new QuadData(
                            index0: v1.Idx,
                            index1: v0.Idx,
                            index2: v4.Idx,
                            index3: vs[v1.SideIdx].Idx,
                            flip: true);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Lateral subdivision - literal port of SubdivideDeck / ClipX / TryAdd /
        // CutAtX (acs/ShipHullPartData.cs:159-284).
        // ------------------------------------------------------------------

        private static IEnumerable<List<ShipVector3>> SubdivideDeck(ShipQuad q)
        {
            float min = 0f;
            float max = 0f;
            ShipVector3[] vertices = q.Vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                float x = vertices[i].X;
                if (x < min)
                {
                    min = x;
                }
                else if (x > max)
                {
                    max = x;
                }
            }

            int startBand = FloorToInt(min / 2f + 0.5f);
            float leftBoundary = min;
            int band = startBand;
            while (true)
            {
                float left = leftBoundary;
                float right = ((float)band + 0.5f) * 2f;
                if (!(right < max) || !(right - left < 1f))
                {
                    leftBoundary = right;
                    if (max - right < 1f)
                    {
                        right = max;
                    }

                    List<ShipVector3> poly = ClipX(q, left, right);
                    if (poly.Count >= 3)
                    {
                        yield return poly;
                    }

                    if (right >= max)
                    {
                        break;
                    }
                }

                band++;
            }
        }

        private static List<ShipVector3> ClipX(ShipQuad quad, float minX, float maxX)
        {
            bool allInside = true;
            ShipVector3[] vertices = quad.Vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                float x = vertices[i].X;
                if (x < minX - float.Epsilon || x > maxX + float.Epsilon)
                {
                    allInside = false;
                    break;
                }
            }

            if (allInside)
            {
                var whole = new List<ShipVector3>(vertices.Length);
                for (int i = 0; i < vertices.Length; i++)
                {
                    whole.Add(vertices[i] + quad.Position);
                }
                return whole;
            }

            var result = new List<ShipVector3>(5);
            int prevZone = -1;
            ShipVector3 prev = new ShipVector3(0f, 0f, 0f);
            for (int j = 0; j < 5; j++)
            {
                ShipVector3 cur = quad.Vertices[j % 4] + quad.Position;
                int zone;
                if (cur.X < minX)
                {
                    zone = 0;
                    if (prevZone == 2)
                    {
                        TryAdd(result, CutAtX(prev, cur, maxX));
                    }
                    if (prevZone == 1 || prevZone == 2)
                    {
                        TryAdd(result, CutAtX(prev, cur, minX));
                    }
                }
                else if (cur.X > maxX)
                {
                    zone = 2;
                    if (prevZone == 0)
                    {
                        TryAdd(result, CutAtX(prev, cur, minX));
                    }
                    if (prevZone == 0 || prevZone == 1)
                    {
                        TryAdd(result, CutAtX(prev, cur, maxX));
                    }
                }
                else
                {
                    zone = 1;
                    switch (prevZone)
                    {
                        case 0:
                            TryAdd(result, CutAtX(prev, cur, minX));
                            break;
                        case 2:
                            TryAdd(result, CutAtX(prev, cur, maxX));
                            break;
                    }
                    TryAdd(result, cur);
                }

                prevZone = zone;
                prev = cur;
            }

            return result;
        }

        private static void TryAdd(List<ShipVector3> result, ShipVector3 value)
        {
            if (result.Count == 0
                || ((value - result[result.Count - 1]).SqrMagnitude > 0.001f
                    && (value - result[0]).SqrMagnitude > 0.001f))
            {
                result.Add(value);
            }
        }

        private static ShipVector3 CutAtX(ShipVector3 a, ShipVector3 b, float x)
        {
            float t = (x - a.X) / (b.X - a.X);
            return ShipVector3.Lerp(a, b, t);
        }

        // ------------------------------------------------------------------
        // Predicates and small helpers - literal ports.
        // ------------------------------------------------------------------

        /// <summary>All four vertices share a y within the strict client tolerance (a deck).</summary>
        public static bool IsDeck(ShipVector3 v0, ShipVector3 v1, ShipVector3 v2, ShipVector3 v3)
            => SameLevel(v0, v1) && SameLevel(v0, v2) && SameLevel(v0, v3);

        private static bool SameLevel(ShipVector3 v0, ShipVector3 v1)
            => Math.Abs(v0.Y - v1.Y) < 0.001f;

        /// <summary>
        /// The two-triangle area metric the client's low-area filter uses
        /// (acs/ShipHullPartData.cs:391-399). Returned as <c>double</c> to match the
        /// client's comparison exactly.
        /// </summary>
        public static double GetArea(ShipVector3 v0, ShipVector3 v1, ShipVector3 v2, ShipVector3 v3)
        {
            ShipVector3 lhs = v1 - v0;
            ShipVector3 rhs = v2 - v0;
            ShipVector3 rhs2 = v3 - v0;
            float a = ShipVector3.Cross(lhs, rhs).Magnitude * 0.5f;
            float b = ShipVector3.Cross(lhs, rhs2).Magnitude * 0.5f;
            return a + b;
        }

        /// <summary>Unity's <c>Mathf.Approximately</c>, ported for the case-2 flat-repair branch only.</summary>
        private static bool Approximately(float a, float b)
        {
            return Math.Abs(b - a)
                < Math.Max(1e-06f * Math.Max(Math.Abs(a), Math.Abs(b)), float.Epsilon * 8f);
        }

        private static ShipVector3 Centroid(IReadOnlyList<ShipVector3> vs)
        {
            ShipVector3 c = new ShipVector3(0f, 0f, 0f);
            for (int i = 0; i < vs.Count; i++)
            {
                c += vs[i];
            }
            return c / vs.Count;
        }

        private static float Lerp1(float a, float b, float t) => (1f - t) * a + t * b;

        /// <summary>Unity's <c>Mathf.FloorToInt</c>: floor then truncate to int.</summary>
        private static int FloorToInt(float f) => (int)Math.Floor(f);

        private static ShipVector3 ToVec(ShipVertexModel v) => new ShipVector3(v.X, v.Y, v.Z);
    }
}
