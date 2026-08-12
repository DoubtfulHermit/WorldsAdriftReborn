using System;
using System.Collections.Generic;
using System.IO;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// A pure, engine-free, in-memory model of a client ShipPlan hull-design blob
    /// with a byte-identical (de)serialiser.
    ///
    /// This is the same blob that:
    ///   - the client's <c>ShipPlan.Save()</c> produces and <c>ShipPlan.Load()</c> reads
    ///     (acs/ShipPlan.cs:82-132 in the decompile),
    ///   - <c>ShipHullSchematicData.field1_data</c> carries (frame designs), and
    ///   - <c>CustomShipHullState.hullData</c> (component 1209) delivers to the client,
    ///     which rebuilds the mesh and colliders from it at runtime.
    ///
    /// EXACT WIRE LAYOUT (all integers little-endian, as .NET BinaryReader/Writer
    /// and the Unity mono client both emit):
    ///
    ///   ShipPlan  = int16 cellCount, then cellCount x Cell
    ///   Cell      = int16 cellNumber(x), int16 deckNumber(y), Section Front,
    ///               bool hasBack, [Section Back if hasBack]
    ///   Section   = Vertex Top[0], Vertex Top[1], Vertex Bottom[0], Vertex Bottom[1],
    ///               sbyte Curve[0,0], Curve[0,1], Curve[1,0], Curve[1,1]
    ///   Vertex    = sbyte x(range 16), sbyte y(range 1.7), sbyte z(range 2)
    ///   Curve     = sbyte offset(range 1)          (see <see cref="ShipQuantize"/>)
    ///
    /// A cell with no Back is 2+2 + 16 + 1 = 21 bytes; with a Back, 2+2 + 16 + 1 + 16
    /// = 37 bytes. The single-cell starter hull is 2 (header) + 37 = 39 bytes.
    ///
    /// On the wire <c>hasBack</c> is "this cell has no astern neighbour" - the
    /// client only writes a Back section for the aft-most cell in a column. This
    /// model preserves that decision losslessly as a nullable <see cref="ShipCellModel.Back"/>:
    /// Back present &lt;=&gt; hasBack true. Decode/Encode do not recompute it, so a
    /// blob round-trips byte-for-byte regardless of whether the neighbour rule was
    /// applied consistently by whatever produced it.
    ///
    /// NOTHING here throws for the caller who uses <see cref="TryDecode"/>: a bad
    /// client blob must never take the server down. <see cref="Decode"/> is the
    /// strict variant that throws a clear <see cref="FormatException"/>; the serve
    /// path should prefer TryDecode.
    /// </summary>
    public sealed class ShipPlanModel
    {
        public List<ShipCellModel> Cells { get; } = new List<ShipCellModel>();

        public ShipPlanModel() { }

        public ShipPlanModel(IEnumerable<ShipCellModel> cells)
        {
            Cells.AddRange(cells);
        }

        /// <summary>
        /// The minimum legal hull: one cell at (0,0), stock section geometry
        /// (half-width 3 m, no curve), Back present. Encodes to the exact 39 bytes
        /// of <c>ShipHull.MinimumHullData()</c> and of make_hulldata.py's
        /// <c>one_cell</c> variant. This is <c>ShipPlan.MakeDefault()</c>.
        /// </summary>
        public static ShipPlanModel MakeDefaultStarterHull()
        {
            var plan = new ShipPlanModel();
            plan.Cells.Add(new ShipCellModel(
                cellNumber: 0,
                deckNumber: 0,
                front: ShipSectionModel.MakeDefault(),
                back: ShipSectionModel.MakeDefault()));
            return plan;
        }

        /// <summary>
        /// Serialise to the exact client wire bytes. Total is deterministic and
        /// depends only on the cell/Back structure, never on geometry values.
        /// </summary>
        public byte[] Encode()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            w.Write((short)Cells.Count);
            foreach (var cell in Cells)
            {
                w.Write((short)cell.CellNumber);
                w.Write((short)cell.DeckNumber);
                WriteSection(w, cell.Front);
                if (cell.Back != null)
                {
                    w.Write(true);
                    WriteSection(w, cell.Back);
                }
                else
                {
                    w.Write(false);
                }
            }

            w.Flush();
            return ms.ToArray();
        }

        /// <summary>
        /// Strict decode. Throws <see cref="ArgumentNullException"/> on null and
        /// <see cref="FormatException"/> on empty/truncated/corrupt input. Use
        /// <see cref="TryDecode"/> on anything that came off the network.
        /// </summary>
        public static ShipPlanModel Decode(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            if (bytes.Length == 0)
            {
                throw new FormatException("ShipPlan blob is empty.");
            }

            try
            {
                using var ms = new MemoryStream(bytes, writable: false);
                using var r = new BinaryReader(ms);

                short cellCount = r.ReadInt16();
                if (cellCount < 0)
                {
                    throw new FormatException($"ShipPlan cell count is negative ({cellCount}).");
                }

                var plan = new ShipPlanModel();
                for (int i = 0; i < cellCount; i++)
                {
                    int cellNumber = r.ReadInt16();
                    int deckNumber = r.ReadInt16();
                    ShipSectionModel front = ReadSection(r);
                    bool hasBack = r.ReadBoolean();
                    ShipSectionModel? back = hasBack ? ReadSection(r) : null;
                    plan.Cells.Add(new ShipCellModel(cellNumber, deckNumber, front, back));
                }

                return plan;
            }
            catch (EndOfStreamException e)
            {
                throw new FormatException("ShipPlan blob is truncated.", e);
            }
        }

        /// <summary>
        /// Server-safe decode: never throws. Returns false with a human-readable
        /// <paramref name="error"/> for a null/empty/truncated/corrupt blob.
        /// </summary>
        public static bool TryDecode(byte[]? bytes, out ShipPlanModel? model, out string? error)
        {
            try
            {
                model = Decode(bytes!);
                error = null;
                return true;
            }
            catch (Exception e)
            {
                model = null;
                error = e.Message;
                return false;
            }
        }

        private static void WriteSection(BinaryWriter w, ShipSectionModel s)
        {
            WriteVertex(w, s.Top[0]);
            WriteVertex(w, s.Top[1]);
            WriteVertex(w, s.Bottom[0]);
            WriteVertex(w, s.Bottom[1]);
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    w.Write(ShipQuantize.SerializeFloat(s.Curve[i, j], ShipQuantize.RangeCurve));
                }
            }
        }

        private static ShipSectionModel ReadSection(BinaryReader r)
        {
            var s = new ShipSectionModel();
            s.Top[0] = ReadVertex(r);
            s.Top[1] = ReadVertex(r);
            s.Bottom[0] = ReadVertex(r);
            s.Bottom[1] = ReadVertex(r);
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    s.Curve[i, j] = ShipQuantize.DeserializeFloat(r.ReadSByte(), ShipQuantize.RangeCurve);
                }
            }
            return s;
        }

        private static void WriteVertex(BinaryWriter w, ShipVertexModel v)
        {
            w.Write(ShipQuantize.SerializeFloat(v.X, ShipQuantize.RangeX));
            w.Write(ShipQuantize.SerializeFloat(v.Y, ShipQuantize.RangeY));
            w.Write(ShipQuantize.SerializeFloat(v.Z, ShipQuantize.RangeZ));
        }

        private static ShipVertexModel ReadVertex(BinaryReader r)
        {
            float x = ShipQuantize.DeserializeFloat(r.ReadSByte(), ShipQuantize.RangeX);
            float y = ShipQuantize.DeserializeFloat(r.ReadSByte(), ShipQuantize.RangeY);
            float z = ShipQuantize.DeserializeFloat(r.ReadSByte(), ShipQuantize.RangeZ);
            return new ShipVertexModel(x, y, z);
        }
    }

    /// <summary>One cell of a ShipPlan. <see cref="Back"/> null means no Back section on the wire.</summary>
    public sealed class ShipCellModel
    {
        public int CellNumber { get; set; }
        public int DeckNumber { get; set; }
        public ShipSectionModel Front { get; set; }
        public ShipSectionModel? Back { get; set; }

        public ShipCellModel(int cellNumber, int deckNumber, ShipSectionModel front, ShipSectionModel? back)
        {
            CellNumber = cellNumber;
            DeckNumber = deckNumber;
            Front = front;
            Back = back;
        }
    }

    /// <summary>
    /// A ShipSection: two Top and two Bottom hull vertices plus a 2x2 grid of
    /// curve offsets. Mirrors acs/ShipSection.cs. Curve[i,j] is written i-outer,
    /// j-inner: [0,0],[0,1],[1,0],[1,1].
    /// </summary>
    public sealed class ShipSectionModel
    {
        public ShipVertexModel[] Top { get; } = new ShipVertexModel[2];
        public ShipVertexModel[] Bottom { get; } = new ShipVertexModel[2];
        public float[,] Curve { get; } = new float[2, 2];

        public ShipSectionModel()
        {
            Top[0] = new ShipVertexModel();
            Top[1] = new ShipVertexModel();
            Bottom[0] = new ShipVertexModel();
            Bottom[1] = new ShipVertexModel();
        }

        /// <summary>
        /// The stock section from the client's <c>ShipSection</c> constructor:
        /// Top/Bottom = (-halfWidth,0,0) and (+halfWidth,0,0), all curves 0.
        /// Default half-width is 3 m.
        /// </summary>
        public static ShipSectionModel MakeDefault(float halfWidth = 3f)
        {
            var s = new ShipSectionModel();
            s.Top[0] = new ShipVertexModel(-halfWidth, 0f, 0f);
            s.Top[1] = new ShipVertexModel(halfWidth, 0f, 0f);
            s.Bottom[0] = new ShipVertexModel(-halfWidth, 0f, 0f);
            s.Bottom[1] = new ShipVertexModel(halfWidth, 0f, 0f);
            return s;
        }
    }

    /// <summary>A single hull vertex. x range 16, y range 1.7, z range 2 when quantised.</summary>
    public sealed class ShipVertexModel
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public ShipVertexModel() { }

        public ShipVertexModel(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
