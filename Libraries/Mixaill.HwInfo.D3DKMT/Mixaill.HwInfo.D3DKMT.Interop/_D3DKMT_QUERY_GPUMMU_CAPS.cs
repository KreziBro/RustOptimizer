using System.Runtime.InteropServices;

namespace Mixaill.HwInfo.D3DKMT.Interop;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct _D3DKMT_QUERY_GPUMMU_CAPS
{
	public uint PhysicalAdapterIndex;

	public _D3DKMT_GPUMMU_CAPS Caps;
}
