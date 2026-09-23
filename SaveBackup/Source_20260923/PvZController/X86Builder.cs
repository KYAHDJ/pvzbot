namespace PvZController;

internal sealed class X86Builder
{
    private readonly List<byte> _code = [];
    private readonly List<(int Position, uint Target)> _calls = [];

    internal void Push(int value)
    {
        _code.Add(0x68);
        AddDword(unchecked((uint)value));
    }

    internal void MovRegisterImmediate(int register, int value)
    {
        _code.Add((byte)(0xB8 + register));
        AddDword(unchecked((uint)value));
    }

    internal void MovRegisterFromAbsolute(int register, uint address)
    {
        _code.Add(0x8B);
        _code.Add((byte)(0x05 + register * 8));
        AddDword(address);
    }

    internal void MovRegisterFromOffset(int register, uint offset)
    {
        _code.Add(0x8B);
        _code.Add((byte)(0x80 + register * 9));
        if (register == 4) _code.Add(0x24);
        AddDword(offset);
    }

    internal void PushRegister(int register) => _code.Add((byte)(0x50 + register));

    internal void Call(uint target)
    {
        _code.Add(0xE8);
        _calls.Add((_code.Count, target));
        AddDword(target);
    }

    internal void Return() => _code.Add(0xC3);

    internal byte[] Build(uint remoteAddress)
    {
        var result = _code.ToArray();
        foreach (var (position, target) in _calls)
        {
            var relative = unchecked((int)(target - (remoteAddress + (uint)position + 4)));
            BitConverter.GetBytes(relative).CopyTo(result, position);
        }
        return result;
    }

    private void AddDword(uint value) => _code.AddRange(BitConverter.GetBytes(value));
}
