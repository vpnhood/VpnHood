using System.Buffers;

namespace VpnHood.Net.Http3;

internal static partial class Http3Qpack
{
    // One decoder for buffered and fragmented fields. Only an incomplete string needs scratch memory;
    // complete buffered strings decode in place, and a declared length never reserves its whole size.
    private sealed class FieldDecoder(int maximumFieldBytes) : IDisposable
    {
        private enum State { Field, Integer, Name, ValueLength, Value }
        private enum IntegerTarget { Indexed, NameIndex, NameLength, ValueLength }

        private State _state;
        private IntegerTarget _integerTarget;
        private ulong _integer;
        private int _integerShift;
        private bool _huffman;
        private int _stringLength;
        private byte[]? _encoded;
        private int _encodedCount;
        private string _name = "";
        private KeyValuePair<string, string> _field;
        private bool _fieldReady;
        private long _sectionRemaining;

        public bool HasPendingField => _state != State.Field;

        public bool ReadByte(byte value, long remaining, out KeyValuePair<string, string> field) =>
            Read([value], remaining, out _, out field);

        public bool Read(ReadOnlySpan<byte> bytes, long remaining, out int consumed,
            out KeyValuePair<string, string> field)
        {
            consumed = 0;
            _sectionRemaining = remaining;
            while (consumed < bytes.Length && !_fieldReady) {
                switch (_state) {
                    case State.Field:
                        _sectionRemaining--;
                        StartField(bytes[consumed++]);
                        break;
                    case State.Integer:
                        _sectionRemaining--;
                        ContinueInteger(bytes[consumed++]);
                        break;
                    case State.ValueLength:
                        _sectionRemaining--;
                        var first = bytes[consumed++];
                        _huffman = (first & 0x80) != 0;
                        StartInteger(first, 7, IntegerTarget.ValueLength);
                        break;
                    case State.Name:
                    case State.Value:
                        var count = ReadString(bytes[consumed..]);
                        consumed += count;
                        _sectionRemaining -= count;
                        break;
                }
            }
            field = _field;
            var completed = _fieldReady;
            _fieldReady = false;
            return completed;
        }

        private void StartField(byte first)
        {
            switch (GetFieldKind(first)) {
                case Http3QpackFieldKind.Indexed:
                    StartInteger(first, 6, IntegerTarget.Indexed);
                    break;
                case Http3QpackFieldKind.NameReference:
                    StartInteger(first, 4, IntegerTarget.NameIndex);
                    break;
                default:
                    _huffman = (first & 0x08) != 0;
                    StartInteger(first, 3, IntegerTarget.NameLength);
                    break;
            }
        }

        private void StartInteger(byte first, int prefixBits, IntegerTarget target)
        {
            var mask = (1 << prefixBits) - 1;
            _integer = (ulong)(first & mask);
            _integerShift = 0;
            _integerTarget = target;
            _state = State.Integer;
            if (_integer < (ulong)mask)
                CompleteInteger();
        }

        private void ContinueInteger(byte next)
        {
            _integer = AppendIntegerGroup(_integer, next, _integerShift);
            if ((next & 0x80) == 0) {
                CompleteInteger();
                return;
            }
            _integerShift += 7;
            if (_integerShift > 56)
                throw new Http3QpackException("A QPACK integer has too many continuation bytes.");
        }

        private void CompleteInteger()
        {
            switch (_integerTarget) {
                case IntegerTarget.Indexed:
                    _field = Http3QpackStaticTable.Get(_integer);
                    _fieldReady = true;
                    _state = State.Field;
                    break;
                case IntegerTarget.NameIndex:
                    _name = Http3QpackStaticTable.Get(_integer).Key;
                    _state = State.ValueLength;
                    break;
                default:
                    if (_integer > (ulong)maximumFieldBytes)
                        throw new Http3QpackException("A QPACK string exceeds the reference's individual string limit.");
                    _stringLength = (int)_integer;
                    if (_stringLength > _sectionRemaining)
                        throw new Http3QpackException("A compressed string is truncated.");
                    _encodedCount = 0;
                    _state = _integerTarget == IntegerTarget.NameLength ? State.Name : State.Value;
                    if (_stringLength == 0)
                        CompleteString("");
                    break;
            }
        }

        private int ReadString(ReadOnlySpan<byte> bytes)
        {
            var count = Math.Min(bytes.Length, _stringLength - _encodedCount);
            if (_encoded == null && count == _stringLength) {
                CompleteString(DecodeFieldString(bytes[..count], _huffman, maximumFieldBytes));
                return count;
            }
            var required = _encodedCount + count;
            if (_encoded == null)
                _encoded = ArrayPool<byte>.Shared.Rent(Math.Min(_stringLength, Math.Max(256, count)));
            else if (required > _encoded.Length) {
                var larger = ArrayPool<byte>.Shared.Rent(Math.Min(_stringLength, Math.Max(required, _encoded.Length * 2)));
                _encoded.AsSpan(0, _encodedCount).CopyTo(larger);
                ArrayPool<byte>.Shared.Return(_encoded);
                _encoded = larger;
            }
            bytes[..count].CopyTo(_encoded.AsSpan(_encodedCount));
            _encodedCount = required;
            if (_encodedCount == _stringLength) {
                var value = DecodeFieldString(_encoded.AsSpan(0, _encodedCount), _huffman, maximumFieldBytes);
                ArrayPool<byte>.Shared.Return(_encoded);
                _encoded = null;
                CompleteString(value);
            }
            return count;
        }

        private void CompleteString(string value)
        {
            if (_state == State.Name) {
                if (value.Length == 0)
                    throw new Http3QpackException("A literal field name is empty.");
                _name = value;
                _state = State.ValueLength;
            }
            else {
                _field = new KeyValuePair<string, string>(_name, value);
                _fieldReady = true;
                _state = State.Field;
            }
        }

        public void Dispose()
        {
            if (_encoded != null) {
                ArrayPool<byte>.Shared.Return(_encoded);
                _encoded = null;
            }
        }
    }
}
