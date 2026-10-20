using System.Runtime.InteropServices;

namespace Mixaill.HwInfo.D3DKMT.Interop;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct _D3DKMT_ADAPTER_PERFDATA
{
	public uint PhysicalAdapterIndex;

	public ulong MemoryFrequency;

	public ulong MaxMemoryFrequency;

	public ulong MaxMemoryFrequencyOC;

	public ulong MemoryBandwidth;

	public ulong PCIEBandwidth;

	public uint FanRPM;

	public uint Power;

	public uint Temperature;

	public byte PowerStateOverride;
}
