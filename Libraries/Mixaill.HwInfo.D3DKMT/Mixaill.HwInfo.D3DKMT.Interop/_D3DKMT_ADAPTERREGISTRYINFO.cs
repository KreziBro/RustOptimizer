using System.Runtime.InteropServices;
using System.Text;

namespace Mixaill.HwInfo.D3DKMT.Interop;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
public struct _D3DKMT_ADAPTERREGISTRYINFO
{
	private unsafe fixed byte adapterString[520];

	private unsafe fixed byte biosString[520];

	private unsafe fixed byte dacType[520];

	private unsafe fixed byte chipType[520];

	public unsafe string AdapterString
	{
		get
		{
			fixed (byte* bytes = adapterString)
			{
				return Encoding.Unicode.GetString(bytes, 520).TrimEnd('\0', ' ');
			}
		}
	}

	public unsafe string BiosString
	{
		get
		{
			fixed (byte* bytes = biosString)
			{
				return Encoding.Unicode.GetString(bytes, 520).TrimEnd('\0', ' ');
			}
		}
	}

	public unsafe string DacType
	{
		get
		{
			fixed (byte* bytes = dacType)
			{
				return Encoding.Unicode.GetString(bytes, 520).TrimEnd('\0', ' ');
			}
		}
	}

	public unsafe string ChipType
	{
		get
		{
			fixed (byte* bytes = chipType)
			{
				return Encoding.Unicode.GetString(bytes, 520).TrimEnd('\0', ' ');
			}
		}
	}
}
