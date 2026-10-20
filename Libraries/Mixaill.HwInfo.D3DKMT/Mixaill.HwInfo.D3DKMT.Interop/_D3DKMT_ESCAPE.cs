using System;

namespace Mixaill.HwInfo.D3DKMT.Interop;

public struct _D3DKMT_ESCAPE
{
	public uint hAdapter;

	public uint hDevice;

	public _D3DKMT_ESCAPETYPE Type;

	public _D3DDDI_ESCAPEFLAGS Flags;

	public IntPtr pPrivateDriverData;

	public uint PrivateDriverDataSize;

	public uint hContext;
}
