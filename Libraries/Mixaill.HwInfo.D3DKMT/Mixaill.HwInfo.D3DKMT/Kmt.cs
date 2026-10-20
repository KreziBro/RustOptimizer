using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Mixaill.HwInfo.D3DKMT.Interop;

namespace Mixaill.HwInfo.D3DKMT;

public class Kmt
{
	private readonly ILogger _logger;

	public Kmt()
	{
	}

	public Kmt(ILogger logger)
	{
		_logger = logger;
	}

	public Kmt(ILogger<Kmt> logger)
	{
		_logger = logger;
	}

	public List<KmtAdapter> GetAdapters()
	{
		List<KmtAdapter> list = new List<KmtAdapter>();
		try
		{
			_D3DKMT_ENUMADAPTERS unnamedParam = default(_D3DKMT_ENUMADAPTERS);
			if (Gdi.D3DKMTEnumAdapters(ref unnamedParam) == NtStatus.STATUS_SUCCESS)
			{
				for (int i = 0; i < unnamedParam.NumAdapters; i++)
				{
					KmtAdapter kmtAdapter = new KmtAdapter(unnamedParam.Adapters[i], _logger);
					if (kmtAdapter.Initialized)
					{
						list.Add(kmtAdapter);
					}
				}
			}
		}
		catch (EntryPointNotFoundException ex)
		{
			_logger?.LogWarning("function not found: " + ex.TargetSite.Name);
		}
		catch (Exception exception)
		{
			_logger?.LogError(exception, "unkown error");
		}
		return list;
	}
}
