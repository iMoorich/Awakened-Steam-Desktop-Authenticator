using System;
using System.IO;
using System.Text;

namespace SteamGuard
{
    /// <summary>
    /// Вспомогательные методы для работы с Protocol Buffers
    /// </summary>
    public static class ProtobufHelper
    {
        public static void WriteTag(Stream ms, int fieldNumber, int wireType)
        {
            WriteVarint(ms, (uint)((fieldNumber << 3) | wireType));
        }

        public static void WriteVarint(Stream ms, uint value)
        {
            while (value >= 0x80)
            {
                ms.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            ms.WriteByte((byte)value);
        }

        public static void WriteVarint(Stream ms, ulong value)
        {
            while (value >= 0x80)
            {
                ms.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            ms.WriteByte((byte)value);
        }

        public static void WriteString(Stream ms, int fieldNumber, string value)
        {
            WriteTag(ms, fieldNumber, 2);
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteVarint(ms, (uint)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }

        public static void WriteFixed64(Stream ms, ulong value)
        {
            ms.WriteByte((byte)(value & 0xFF));
            ms.WriteByte((byte)((value >> 8) & 0xFF));
            ms.WriteByte((byte)((value >> 16) & 0xFF));
            ms.WriteByte((byte)((value >> 24) & 0xFF));
            ms.WriteByte((byte)((value >> 32) & 0xFF));
            ms.WriteByte((byte)((value >> 40) & 0xFF));
            ms.WriteByte((byte)((value >> 48) & 0xFF));
            ms.WriteByte((byte)((value >> 56) & 0xFF));
        }

        public static void WriteSInt32(Stream ms, int value)
        {
            uint v = (uint)((value << 1) ^ (value >> 31));
            WriteVarint(ms, v);
        }

        public static void WriteSInt64(Stream ms, long value)
        {
            ulong v = (ulong)((value << 1) ^ (value >> 63));
            WriteVarint(ms, v);
        }

        public static uint ReadVarint32(BinaryReader reader)
        {
            uint result = 0;
            int shift = 0;
            while (true)
            {
                byte b = reader.ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
            }
            return result;
        }

        public static ulong ReadVarint(BinaryReader reader)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte b = reader.ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
                if (shift >= 64)
                    throw new OverflowException("Varint too long");
            }
            return result;
        }

        public static string ReadString(BinaryReader reader)
        {
            int length = (int)ReadVarint32(reader);
            var bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        public static void SkipField(BinaryReader reader, int wireType)
        {
            switch (wireType)
            {
                case 0:
                    ReadVarint(reader);
                    break;
                case 1:
                    reader.ReadBytes(8);
                    break;
                case 2:
                    int len = (int)ReadVarint32(reader);
                    reader.ReadBytes(len);
                    break;
                case 5:
                    reader.ReadBytes(4);
                    break;
            }
        }
    }
}
