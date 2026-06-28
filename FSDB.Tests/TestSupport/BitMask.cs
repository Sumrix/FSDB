namespace FSDB.Tests.TestSupport;

public readonly struct BitMask
{
    private readonly uint _value;

    public BitMask(uint value)
    {
        _value = value;
    }

    public bool this[int bit]
        => ((_value >> bit) & 1) != 0;

    public static implicit operator BitMask(uint value)
        => new(value);

    public static implicit operator uint(BitMask value)
        => value._value;
}