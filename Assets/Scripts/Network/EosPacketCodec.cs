using System;

/// <summary>EOSの1170バイト上限に合わせる。再構築状態は送信者・channelごとに保持する。</summary>
public static class EosPacketCodec
{
    public const int HeaderSize = 12;
    public const int MaxMessageSize = 1024 * 1024;

    public static void WriteUInt(byte[] data, int offset, uint value)
    {
        for (int i = 0; i < 4; i++) data[offset + i] = (byte)(value >> (i * 8));
    }

    public static uint ReadUInt(byte[] data, int offset)
    {
        uint value = 0;
        for (int i = 0; i < 4; i++) value |= (uint)data[offset + i] << (i * 8);
        return value;
    }

    public static byte[] Fragment(ArraySegment<byte> message, uint id, int offset, int packetSize)
    {
        int count = Math.Min(message.Count - offset, packetSize - HeaderSize);
        var packet = new byte[HeaderSize + count];
        WriteUInt(packet, 0, id);
        WriteUInt(packet, 4, (uint)message.Count);
        WriteUInt(packet, 8, (uint)offset);
        Buffer.BlockCopy(message.Array, message.Offset + offset, packet, HeaderSize, count);
        return packet;
    }

    public sealed class Reader
    {
        private uint messageId;
        private byte[] buffer;
        private int received;
        private uint lastSequence;
        private bool hasSequence;

        public byte[] Receive(byte[] packet, int length, bool sequenced)
        {
            if (length < HeaderSize || length > packet.Length) return null;
            uint id = ReadUInt(packet, 0), total = ReadUInt(packet, 4), offset = ReadUInt(packet, 8);
            int count = length - HeaderSize;
            if (total == 0 || total > MaxMessageSize || offset > total || count > total - offset)
                return null;
            // UnreliableSequencedは分割しない。uintの周回を考慮する。
            if (sequenced && (offset != 0 || count != total
                || (hasSequence && unchecked((int)(id - lastSequence)) <= 0))) return null;
            if (offset == 0)
            {
                messageId = id;
                buffer = new byte[total];
                received = 0;
            }
            if (buffer == null || id != messageId || buffer.Length != total || offset != received)
                return null;
            Buffer.BlockCopy(packet, HeaderSize, buffer, received, count);
            received += count;
            if (received != buffer.Length) return null;
            var complete = buffer;
            buffer = null;
            lastSequence = id;
            hasSequence = true;
            return complete;
        }
    }
}
