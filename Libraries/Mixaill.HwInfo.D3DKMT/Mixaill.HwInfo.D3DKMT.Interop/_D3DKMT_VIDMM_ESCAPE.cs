using System.Runtime.InteropServices;
using Mixaill.HwInfo.D3DKMT.Helpers;

namespace Mixaill.HwInfo.D3DKMT.Interop;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct _D3DKMT_VIDMM_ESCAPE
{
	public _D3DKMT_VIDMMESCAPETYPE Type;

	private unsafe fixed byte data[120];

	public unsafe _D3DKMT_VIDMM_ESCAPE_GETVADS GetVads
	{
		get
		{
			fixed (byte* bytes = data)
			{
				return InteropHelper.Get<_D3DKMT_VIDMM_ESCAPE_GETVADS>(bytes, 0, sizeof(_D3DKMT_VIDMM_ESCAPE_GETVADS));
			}
		}
		set
		{
			fixed (byte* bytes = data)
			{
				InteropHelper.Set(value, bytes, 0, sizeof(_D3DKMT_VIDMM_ESCAPE_GETVADS));
			}
		}
	}
}
