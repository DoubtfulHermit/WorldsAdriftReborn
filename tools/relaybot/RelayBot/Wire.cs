using ProtoBuf;

namespace RelayBot
{
    /// <summary>
    /// The OUTER wire framing: protobuf-net mirrors of the six .proto messages in
    /// WorldsAdriftRebornCoreSdk/*.proto, one ENet channel each (enetLayer.h).
    ///
    /// WHY MIRRORS AND NOT THE DLL. The shim's PB_EXP_* exports only cover the
    /// SERVER's direction (serialize the ops a server sends, deserialize the ops
    /// a server receives). The client direction lives behind the
    /// WorkerProtocol_Connection surface, whose OpList/vtable structs differ in
    /// layout between the Windows DLL and a native .so (LLP64 vs LP64 `long`).
    /// These messages are five trivial proto3 records; declaring them here in
    /// managed code sidesteps that entire marshaling problem, and the wire bytes
    /// are what protoc would produce for the same fields.
    ///
    /// Field numbers are copied from the .proto files and MUST track them.
    /// </summary>
    [ProtoContract]
    public class PbAssetLoadRequestOp
    {
        [ProtoMember(1)] public string AssetType;
        [ProtoMember(2)] public string Name;
        [ProtoMember(3)] public string Context;
        [ProtoMember(4)] public string Url;
    }

    [ProtoContract]
    public class PbAddEntityOp
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public string PrefabName;
        [ProtoMember(3)] public string PrefabContext;
    }

    [ProtoContract]
    public class PbInterestOverride
    {
        [ProtoMember(1)] public uint ComponentId;
        [ProtoMember(2)] public bool IsInterested;
    }

    [ProtoContract]
    public class PbSendComponentInterest
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public List<PbInterestOverride> Components = new();
    }

    /// <summary>
    /// One entry of an AddComponentOp / ComponentUpdateOp batch. The two .proto
    /// messages (ComponentData and ComponentData_ComponentUpdate) have identical
    /// shapes, so one mirror serves both.
    /// </summary>
    [ProtoContract]
    public class PbComponentData
    {
        [ProtoMember(1)] public uint ComponentId;
        [ProtoMember(2)] public byte[] Data;
        [ProtoMember(3)] public int DataLength;
    }

    [ProtoContract]
    public class PbComponentBatchOp // AddComponentOp and ComponentUpdateOp
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public List<PbComponentData> Components = new();
    }

    [ProtoContract]
    public class PbAuthorityChange
    {
        [ProtoMember(1)] public uint ComponentId;
        [ProtoMember(2)] public bool HasAuthority;
    }

    [ProtoContract]
    public class PbAuthorityChangeOpWrapper
    {
        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public List<PbAuthorityChange> OpList = new();
    }

    [ProtoContract]
    public class PbRemoveEntityOp
    {
        [ProtoMember(1)] public long EntityId;
    }

    public static class Wire
    {
        public static byte[] Encode<T>(T message)
        {
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, message);
            return ms.ToArray();
        }

        public static T Decode<T>(byte[] data, int length)
        {
            using var ms = new MemoryStream(data, 0, length, writable: false);
            return Serializer.Deserialize<T>(ms);
        }
    }
}
