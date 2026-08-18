using Unity.Collections;
using Unity.Netcode;

public struct ResultEntry : INetworkSerializable
{
    public FixedString64Bytes Name;
    public int Score;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Name);
        serializer.SerializeValue(ref Score);
    }
}

public struct RosterEntry : INetworkSerializable
{
    public ulong ClientId;
    public FixedString64Bytes Name;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Name);
    }
}
