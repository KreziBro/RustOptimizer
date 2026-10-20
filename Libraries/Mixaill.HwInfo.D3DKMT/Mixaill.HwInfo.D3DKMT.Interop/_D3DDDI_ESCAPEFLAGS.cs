using System;

namespace Mixaill.HwInfo.D3DKMT.Interop;

[Flags]
public enum _D3DDDI_ESCAPEFLAGS : uint
{
	None = 0u,
	HardwareAccess = 1u,
	DeviceStatusQuery = 2u,
	ChangeFrameLatency = 4u,
	NoAdapterSynchronization = 8u,
	VirtualMachineData = 0x20u,
	DriverKnownEscape = 0x40u,
	DriverCommonEscape = 0x80u
}
