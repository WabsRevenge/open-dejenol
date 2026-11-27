using System.Text;

namespace Mordor.Core;

/// <summary>
/// Writes VB3 random-access (.MDR) records: the mirror of <see cref="RecordReader"/>. A record is
/// filled field by field into a buffer of the record length and then written whole, as VB3's
/// <c>Put #n, rec, var</c> writes it. Integers are little-endian, strings carry a 16-bit length in
/// front (VB3's variable-length string inside a user type), and the unused tail of a record is
/// zeros. A field that would run past the end of its record throws rather than being cut, since
/// <see cref="RecordReader"/> would then read a different value back.
///
/// The records are built in memory; <see cref="Save"/> writes the whole file at once through
/// <see cref="SafeFile"/>, as every file the port writes is written.
/// </summary>
public sealed class RecordWriter
{
    private readonly byte[] _buffer;
    private readonly MemoryStream _file = new();
    private readonly int _recordLength;
    private int _cursor;

    public RecordWriter(int recordLength)
    {
        if (recordLength <= 0) throw new ArgumentOutOfRangeException(nameof(recordLength));
        _recordLength = recordLength;
        _buffer = new byte[recordLength];
    }

    /// <summary>Write the record built so far (the whole record length) and start a fresh, zeroed one.</summary>
    public void Write()
    {
        _file.Write(_buffer, 0, _recordLength);
        Array.Clear(_buffer);
        _cursor = 0;
    }

    /// <summary>Move to the 1-based record number, as <see cref="RecordReader.Seek"/> does; a gap before it reads as zeros.</summary>
    public void Seek(long recnum) => _file.Seek((long)_recordLength * (recnum - 1), SeekOrigin.Begin);

    /// <summary>The file as built so far.</summary>
    public byte[] ToArray() => _file.ToArray();

    /// <summary>Write the file out whole (<see cref="SafeFile.WriteAllBytes"/>).</summary>
    public void Save(string path) => SafeFile.WriteAllBytes(path, ToArray());

    private void Room(int n)
    {
        if (_cursor + n > _recordLength)
            throw new InvalidDataException($"a field of {n} bytes at {_cursor} runs past the {_recordLength}-byte record");
    }

    public void PutByte(byte v) { Room(1); _buffer[_cursor++] = v; }

    public void PutWord(short v) { Room(2); _buffer[_cursor] = (byte)v; _buffer[_cursor + 1] = (byte)(v >> 8); _cursor += 2; }

    public void PutUWord(int v)
    {
        if (v < 0 || v > ushort.MaxValue) throw new InvalidDataException($"{v} does not fit an unsigned 16-bit field");
        PutWord(unchecked((short)v));
    }

    public void PutDword(int v)
    {
        Room(4);
        for (int i = 0; i < 4; i++) _buffer[_cursor + i] = (byte)(v >> (8 * i));
        _cursor += 4;
    }

    public void PutInt64(long v)
    {
        Room(8);
        for (int i = 0; i < 8; i++) _buffer[_cursor + i] = (byte)(v >> (8 * i));
        _cursor += 8;
    }

    public void PutFloat(float v)
    {
        Room(4);
        BitConverter.TryWriteBytes(_buffer.AsSpan(_cursor, 4), v);
        _cursor += 4;
    }

    /// <summary>VB3 Currency, raw: a 64-bit integer in 1/10000 units (<see cref="RecordReader.GetCurrency"/>).</summary>
    public void PutCurrency(long raw) => PutInt64(raw);

    /// <summary>VB3 Currency holding a whole number, the inverse of <see cref="RecordReader.GetIntCurrency"/>.</summary>
    public void PutIntCurrency(long v) => PutInt64(checked(v * 10000));

    /// <summary>
    /// A string. With no length, a 16-bit length prefix and then its bytes; with one, exactly that
    /// many bytes, padded with spaces as VB3 pads a fixed-length string.
    /// </summary>
    public void PutString(string s, int length = 0)
    {
        byte[] b = Encoding.Latin1.GetBytes(s);
        if (length == 0)
        {
            PutUWord(b.Length);
            Room(b.Length);
            b.CopyTo(_buffer, _cursor);
            _cursor += b.Length;
            return;
        }
        if (b.Length > length) throw new InvalidDataException($"\"{s}\" is longer than its {length}-byte field");
        Room(length);
        b.CopyTo(_buffer, _cursor);
        for (int i = b.Length; i < length; i++) _buffer[_cursor + i] = (byte)' ';
        _cursor += length;
    }

    /// <summary>
    /// Exactly <paramref name="count"/> words: the array's, then noughts for any it does not have
    /// (a model that keeps fewer than the record holds, as <see cref="Race.Min"/> keeps six of seven).
    /// More than the record holds throws, as they could not be read back.
    /// </summary>
    public void PutWords(IReadOnlyList<short> a, int count)
    {
        if (a.Count > count) throw new InvalidDataException($"{a.Count} values for a field of {count} words");
        for (int i = 0; i < count; i++) PutWord(i < a.Count ? a[i] : (short)0);
    }

    /// <summary><see cref="PutWords(IReadOnlyList{short}, int)"/> for a model that holds them as Integers.</summary>
    public void PutWords(IReadOnlyList<int> a, int count)
    {
        if (a.Count > count) throw new InvalidDataException($"{a.Count} values for a field of {count} words");
        for (int i = 0; i < count; i++) PutWord(i < a.Count ? checked((short)a[i]) : (short)0);
    }
}
