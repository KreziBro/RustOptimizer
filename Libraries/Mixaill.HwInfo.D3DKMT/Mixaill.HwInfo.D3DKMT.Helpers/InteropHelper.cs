using System;
using System.Runtime.InteropServices;

namespace Mixaill.HwInfo.D3DKMT.Helpers;

public static class InteropHelper
{
	internal unsafe static T Get<T>(byte* bytes, int offset, int count)
	{
		if (bytes == null)
		{
			return default(T);
		}
		if (!typeof(T).IsValueType)
		{
			throw new NotSupportedException();
		}
		byte[] array = new byte[Marshal.SizeOf<T>()];
		for (int i = 0; i < count; i++)
		{
			array[i] = bytes[offset + i];
		}
		GCHandle gCHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
		try
		{
			return (T)Marshal.PtrToStructure(gCHandle.AddrOfPinnedObject(), typeof(T));
		}
		finally
		{
			gCHandle.Free();
		}
	}

	public unsafe static void Set<T>(T obj, byte* bytes, int offset, int count)
	{
		if (!typeof(T).IsValueType)
		{
			throw new ArgumentException(null, "T");
		}
		byte[] array = new byte[Marshal.SizeOf<T>()];
		IntPtr intPtr = Marshal.AllocCoTaskMem(array.Length);
		try
		{
			Marshal.StructureToPtr(obj, intPtr, fDeleteOld: false);
			Marshal.Copy(intPtr, array, 0, array.Length);
			for (int i = 0; i < array.Length; i++)
			{
				bytes[i] = array[i];
			}
		}
		finally
		{
			Marshal.FreeCoTaskMem(intPtr);
		}
	}
}
