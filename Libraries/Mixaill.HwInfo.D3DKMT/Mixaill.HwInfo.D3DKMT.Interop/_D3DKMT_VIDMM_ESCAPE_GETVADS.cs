using Mixaill.HwInfo.D3DKMT.Helpers;

namespace Mixaill.HwInfo.D3DKMT.Interop;

public struct _D3DKMT_VIDMM_ESCAPE_GETVADS
{
	public unsafe fixed byte data[112];

	public _D3DKMT_VAD_ESCAPE_COMMAND Command;

	public int Status;

	public unsafe _D3DKMT_VIDMM_ESCAPE_GETVADS_GETNUMVADS GetNumVads
	{
		get
		{
			fixed (byte* bytes = data)
			{
				return InteropHelper.Get<_D3DKMT_VIDMM_ESCAPE_GETVADS_GETNUMVADS>(bytes, 0, sizeof(_D3DKMT_VIDMM_ESCAPE_GETVADS_GETNUMVADS));
			}
		}
		set
		{
			fixed (byte* bytes = data)
			{
				InteropHelper.Set(value, bytes, 0, sizeof(_D3DKMT_VIDMM_ESCAPE_GETVADS_GETNUMVADS));
			}
		}
	}
}
