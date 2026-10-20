using System.Runtime.InteropServices;

namespace Mixaill.HwInfo.D3DKMT.Interop;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct _D3DKMT_ADAPTER_PERFDATACAPS
{
	public uint PhysicalAdapterIndex;

	public ulong MaxMemoryBandwidth;

	public ulong MaxPCIEBandwidth;

	public uint MaxFanRPM;

	public uint TemperatureMax;

	public uint TemperatureWarning;
}
