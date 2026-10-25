using System.Buffers.Binary;
using System.Reflection.Metadata;

namespace RefTrace.Metadata;

internal sealed record IlCall(int Offset, ILOpCode Instruction, int Token);

internal static class IlReader
{
    public static IReadOnlyList<IlCall> Calls(ReadOnlySpan<byte> bytes)
    {
        var calls = new List<IlCall>();
        var position = 0;
        while (position < bytes.Length)
        {
            var offset = position;
            var code = (int)bytes[position++];
            if (code == 0xfe)
            {
                Require(bytes, position, 1);
                code = 0xfe00 | bytes[position++];
            }
            var instruction = (ILOpCode)code;
            if (!Enum.IsDefined(instruction)) throw new BadImageFormatException($"Unknown IL opcode at IL_{offset:x4}.");
            if (code == 0x45)
            {
                Require(bytes, position, 4);
                var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[position..]);
                position += 4;
                if (count < 0 || count > (bytes.Length - position) / 4)
                    throw new BadImageFormatException($"Invalid switch operand at IL_{offset:x4}.");
                position += count * 4;
                continue;
            }
            var size = OperandSize(code);
            Require(bytes, position, size);
            if (code is 0x27 or 0x28 or 0x6f or 0x73 or 0xfe06 or 0xfe07)
                calls.Add(new(offset, instruction, BinaryPrimitives.ReadInt32LittleEndian(bytes[position..])));
            position += size;
        }
        return calls;
    }

    private static int OperandSize(int code) => code switch
    {
        >= 0x0e and <= 0x13 or 0x1f or >= 0x2b and <= 0x37 or 0xde or 0xfe12 or 0xfe19 => 1,
        >= 0xfe09 and <= 0xfe0e => 2,
        0x21 or 0x23 => 8,
        0x20 or 0x22 or 0x27 or 0x28 or 0x29 or >= 0x38 and <= 0x44
            or 0x6f or 0x70 or 0x71 or 0x72 or 0x73 or 0x74 or 0x75 or 0x79
            or >= 0x7b and <= 0x81 or 0x8c or 0x8d or 0x8f
            or 0xa3 or 0xa4 or 0xa5 or 0xc2 or 0xc6 or 0xd0 or 0xdd
            or 0xfe06 or 0xfe07 or 0xfe15 or 0xfe16 or 0xfe1c => 4,
        _ => 0
    };

    private static void Require(ReadOnlySpan<byte> bytes, int start, int count)
    {
        if (start < 0 || count < 0 || count > bytes.Length - start)
            throw new BadImageFormatException("Truncated IL operand.");
    }
}
