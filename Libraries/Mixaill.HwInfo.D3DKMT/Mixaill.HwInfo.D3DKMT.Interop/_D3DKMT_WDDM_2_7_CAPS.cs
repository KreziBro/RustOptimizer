using System.Runtime.InteropServices;
using Mixaill.HwInfo.D3DKMT.Helpers;

namespace Mixaill.HwInfo.D3DKMT.Interop;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct _D3DKMT_WDDM_2_7_CAPS
{
	private uint data;

	public bool HagsSupported => data.IsBitSet(0);

	public bool HagsEnabled => data.IsBitSet(1);
}
