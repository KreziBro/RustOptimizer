using System;
using Microsoft.Extensions.Logging;
using Mixaill.HwInfo.D3DKMT.Interop;

namespace Mixaill.HwInfo.D3DKMT;

public class KmtAdapter : IDisposable
{
	private readonly ILogger _logger;

	public _D3DKMT_ADAPTERINFO AdapterInfo;

	public _D3DKMT_ADAPTERREGISTRYINFO AdapterRegistryInfo;

	public _D3DKMT_ADAPTERTYPE AdapterType;

	public _D3DKMT_DEVICE_IDS DeviceIds;

	public _D3DKMT_DRIVERVERSION DriverVersion = _D3DKMT_DRIVERVERSION.KMT_DRIVERVERSION_WDDM_1_0;

	public _D3DKMT_GPUMMU_CAPS GpuMmuCapabilities;

	public _D3DKMT_ADAPTER_PERFDATACAPS PerformanceDataCapabilities;

	public _D3DKMT_WDDM_2_7_CAPS WddmCapabilities_27;

	private bool disposedValue;

	public bool Initialized { get; private set; }

	public KmtAdapter(_D3DKMT_ADAPTERINFO AdapterInfo, ILogger logger)
	{
		_logger = logger;
		initialize(AdapterInfo);
	}

	public KmtAdapter(_D3DKMT_ADAPTERINFO AdapterInfo, ILogger<KmtAdapter> logger)
	{
		_logger = logger;
		initialize(AdapterInfo);
	}

	private void initialize(_D3DKMT_ADAPTERINFO AdapterInfo)
	{
		this.AdapterInfo = AdapterInfo;
		try
		{
			AdapterRegistryInfo = getAdapterRegistryInfo();
			DriverVersion = getDriverVersion();
			AdapterType = getAdapterType();
			DeviceIds = getDeviceIds();
			GpuMmuCapabilities = getGpuMmuCapabilities();
			PerformanceDataCapabilities = getPerformanceDataCapabilities();
			WddmCapabilities_27 = getWddmCapabilities27();
		}
		catch (Exception exception)
		{
			_logger?.LogError(exception, "unknown error");
			return;
		}
		Initialized = true;
	}

	private bool adapterClose()
	{
		_D3DKMT_CLOSEADAPTER unnamedParam = default(_D3DKMT_CLOSEADAPTER);
		unnamedParam.hAdapter = AdapterInfo.hAdapter;
		return Gdi.D3DKMTCloseAdapter(ref unnamedParam) == NtStatus.STATUS_SUCCESS;
	}

	private unsafe _D3DKMT_ADAPTERREGISTRYINFO getAdapterRegistryInfo()
	{
		_D3DKMT_ADAPTERREGISTRYINFO result = default(_D3DKMT_ADAPTERREGISTRYINFO);
		if (DriverVersion >= _D3DKMT_DRIVERVERSION.KMT_DRIVERVERSION_WDDM_1_0)
		{
			queryAdapterInfo(_D3DKMT_QUERYADAPTERINFOTYPE.KMTQAITYPE_ADAPTERREGISTRYINFO, (IntPtr)(&result), sizeof(_D3DKMT_ADAPTERREGISTRYINFO));
		}
		return result;
	}

	private unsafe _D3DKMT_DRIVERVERSION getDriverVersion()
	{
		_D3DKMT_DRIVERVERSION result = (_D3DKMT_DRIVERVERSION)0u;
		if (DriverVersion >= _D3DKMT_DRIVERVERSION.KMT_DRIVERVERSION_WDDM_1_0)
		{
			queryAdapterInfo(_D3DKMT_QUERYADAPTERINFOTYPE.KMTQAITYPE_DRIVERVERSION, (IntPtr)(&result), 4);
		}
		return result;
	}

	private unsafe _D3DKMT_ADAPTERTYPE getAdapterType()
	{
		_D3DKMT_ADAPTERTYPE result = default(_D3DKMT_ADAPTERTYPE);
		if (DriverVersion >= _D3DKMT_DRIVERVERSION.KMT_DRIVERVERSION_WDDM_1_2)
		{
			queryAdapterInfo(_D3DKMT_QUERYADAPTERINFOTYPE.KMTQAITYPE_ADAPTERTYPE, (IntPtr)(&result), sizeof(_D3DKMT_ADAPTERTYPE));
		}
		return result;
	}

	private unsafe _D3DKMT_DEVICE_IDS getDeviceIds()
	{
		_D3DKMT_QUERY_DEVICE_IDS d3DKMT_QUERY_DEVICE_IDS = default(_D3DKMT_QUERY_DEVICE_IDS);
		if (DriverVersion >= _D3DKMT_DRIVERVERSION.KMT_DRIVERVERSION_WDDM_2_0)
		{
			queryAdapterInfo(_D3DKMT_QUERYADAPTERINFOTYPE.KMTQAITYPE_PHYSICALADAPTERDEVICEIDS, (IntPtr)(&d3DKMT_QUERY_DEVICE_IDS), sizeof(_D3DKMT_QUERY_DEVICE_IDS));
		}
		return d3DKMT_QUERY_DEVICE_IDS.DeviceIds;
	}

	private unsafe _D3DKMT_GPUMMU_CAPS getGpuMmuCapabilities()
	{
		_D3DKMT_QUERY_GPUMMU_CAPS d3DKMT_QUERY_GPUMMU_CAPS = default(_D3DKMT_QUERY_GPUMMU_CAPS);
		if (DriverVersion >= _D3DKMT_DRIVERVERSION.KMT_DRIVERVERSION_WDDM_2_0)
		{
			queryAdapterInfo(_D3DKMT_QUERYADAPTERINFOTYPE.KMTQAITYPE_QUERY_GPUMMU_CAPS, (IntPtr)(&d3DKMT_QUERY_GPUMMU_CAPS), sizeof(_D3DKMT_QUERY_GPUMMU_CAPS));
		}
		return d3DKMT_QUERY_GPUMMU_CAPS.Caps;
	}

	public unsafe (bool, _D3DKMT_ADAPTER_PERFDATA) GetPerformanceData()
	{
		bool item = false;
		_D3DKMT_ADAPTER_PERFDATA item2 = default(_D3DKMT_ADAPTER_PERFDATA);
		if (DriverVersion >= _D3DKMT_DRIVERVERSION.KMT_DRIVERVERSION_WDDM_2_4)
		{
			item = queryAdapterInfo(_D3DKMT_QUERYADAPTERINFOTYPE.KMTQAITYPE_ADAPTERPERFDATA, (IntPtr)(&item2), sizeof(_D3DKMT_ADAPTER_PERFDATA));
		}
		return (item, item2);
	}

	private unsafe _D3DKMT_ADAPTER_PERFDATACAPS getPerformanceDataCapabilities()
	{
		_D3DKMT_ADAPTER_PERFDATACAPS result = default(_D3DKMT_ADAPTER_PERFDATACAPS);
		if (DriverVersion >= _D3DKMT_DRIVERVERSION.KMT_DRIVERVERSION_WDDM_2_4)
		{
			queryAdapterInfo(_D3DKMT_QUERYADAPTERINFOTYPE.KMTQAITYPE_ADAPTERPERFDATA_CAPS, (IntPtr)(&result), sizeof(_D3DKMT_ADAPTER_PERFDATACAPS));
		}
		return result;
	}

	private unsafe _D3DKMT_WDDM_2_7_CAPS getWddmCapabilities27()
	{
		_D3DKMT_WDDM_2_7_CAPS result = default(_D3DKMT_WDDM_2_7_CAPS);
		if (DriverVersion >= _D3DKMT_DRIVERVERSION.KMT_DRIVERVERSION_WDDM_2_7)
		{
			queryAdapterInfo(_D3DKMT_QUERYADAPTERINFOTYPE.KMTQAITYPE_WDDM_2_7_CAPS, (IntPtr)(&result), sizeof(_D3DKMT_WDDM_2_7_CAPS));
		}
		return result;
	}

	private bool queryAdapterInfo(_D3DKMT_QUERYADAPTERINFOTYPE requestType, IntPtr buf, int size)
	{
		_D3DKMT_QUERYADAPTERINFO unnamedParam = new _D3DKMT_QUERYADAPTERINFO
		{
			hAdapter = AdapterInfo.hAdapter,
			Type = requestType,
			pPrivateDriverData = buf,
			PrivateDriverDataSize = (uint)size
		};
		NtStatus ntStatus = NtStatus.STATUS_SUCCESS;
		try
		{
			ntStatus = Gdi.D3DKMTQueryAdapterInfo(ref unnamedParam);
		}
		catch (Exception exception)
		{
			_logger?.LogError(exception, $"failed to get adapterInfo, hAdapter={AdapterInfo.hAdapter}, requestType={requestType}");
			return false;
		}
		switch (ntStatus)
		{
		case NtStatus.STATUS_SUCCESS:
			return true;
		case NtStatus.STATUS_OBJECT_NAME_NOT_FOUND:
			return false;
		default:
			_logger?.LogWarning($"failed to get adapterInfo, hAdapter=0x{AdapterInfo.hAdapter:X8}, requestType={requestType}, result={ntStatus}");
			return false;
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposedValue)
		{
			adapterClose();
			disposedValue = true;
		}
	}

	~KmtAdapter()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
