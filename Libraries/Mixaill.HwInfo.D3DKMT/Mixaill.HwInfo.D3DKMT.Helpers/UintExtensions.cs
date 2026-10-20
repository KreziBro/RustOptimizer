namespace Mixaill.HwInfo.D3DKMT.Helpers;

public static class UintExtensions
{
	public static bool IsBitSet(this uint data, int pos)
	{
		return ((data >> pos) & 1) != 0;
	}

	public static uint SetBit(this uint data, int pos, bool val)
	{
		data = ((!val) ? (data & (uint)(~(1 << pos))) : (data | (uint)(1 << pos)));
		return data;
	}
}
