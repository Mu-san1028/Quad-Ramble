using Unity.Collections;
using Unity.Netcode;

/// <summary>結果演出用の、加点前後の確定値。</summary>
public struct RoundScoreEntry : INetworkSerializable
{
    public ulong ClientId;
    public ulong PlayerId;
    public int Before;
    public int After;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref PlayerId);
        serializer.SerializeValue(ref Before);
        serializer.SerializeValue(ref After);
    }
}

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
    public ulong PlayerId;
    public int Score;
    public ulong ClientId;
    public FixedString64Bytes Name;
    public int CharacterIndex;
    public bool IsReady;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref PlayerId);
        serializer.SerializeValue(ref Score);
        serializer.SerializeValue(ref Name);
        serializer.SerializeValue(ref CharacterIndex);
        serializer.SerializeValue(ref IsReady);
    }
}
