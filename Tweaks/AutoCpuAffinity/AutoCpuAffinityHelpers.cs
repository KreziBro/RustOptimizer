using System.Runtime.InteropServices;
using RustOptimizer.Rust;

namespace RustOptimizer.Tweaks;

internal readonly record struct AcpuLogicalCpu(ushort Group, int Index)
{
    public override string ToString() => Group != 0 ? $"{Group}:{Index}" : Index.ToString();
}

internal sealed record AcpuCpuSet(
    uint Id,
    ushort Group,
    byte LogicalProcessorIndex,
    byte CoreIndex,
    byte LastLevelCacheIndex,
    byte NumaNodeIndex,
    byte EfficiencyClass)
{
    public AcpuLogicalCpu LogicalCpu => new(Group, LogicalProcessorIndex);
}

internal sealed record AcpuL3Group(
    ushort Group,
    uint CacheSizeBytes,
    int CacheType,
    ushort LineSize,
    byte Associativity,
    nuint SharedMask)
{
    public IReadOnlyList<AcpuLogicalCpu> LogicalCpus =>
        AcpuNative.GetLogicalProcessors(SharedMask).Select(idx => new AcpuLogicalCpu(Group, idx)).ToArray();
}

internal sealed record AcpuRecommendation(
    string ProcessorName,
    IReadOnlyList<AcpuCpuSet> CpuSets,
    IReadOnlyList<AcpuL3Group> L3Caches,
    IReadOnlyList<AcpuLogicalCpu> PerformanceLogicalCpus,
    IReadOnlyList<AcpuLogicalCpu> TargetL3LogicalCpus,
    IReadOnlyList<AcpuLogicalCpu> SelectedLogicalCpus,
    IReadOnlyList<AcpuLogicalCpu> OneLogicalCpuPerPhysicalCore);

internal static class AcpuSteam
{
    public static string GetClientCfgPath() => SteamAndRustService.GetClientCfgPath();
    public static string GetLocalConfigPath() => SteamAndRustService.GetConfigPathToLastUser();
    public static string ReadLaunchOptions() => SteamAndRustService.ReadLaunchOptions();
    public static void WriteLaunchOptions(string launchOptions) => SteamAndRustService.WriteLaunchOptions(launchOptions);
}

internal static class AcpuNative
{
    const int RelationCache = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct SystemCpuSetInformation
    {
        public uint Size;
        public int Type;
        public uint Id;
        public ushort Group;
        public byte LogicalProcessorIndex;
        public byte CoreIndex;
        public byte LastLevelCacheIndex;
        public byte NumaNodeIndex;
        public byte EfficiencyClass;
        public byte AllFlags;
        public uint Reserved;
        public ulong AllocationTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GROUP_AFFINITY
    {
        public nuint Mask;
        public ushort Group;
        public ushort Reserved0;
        public ushort Reserved1;
        public ushort Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CACHE_RELATIONSHIP
    {
        public byte Level;
        public byte Associativity;
        public ushort LineSize;
        public uint CacheSize;
        public int Type;
        public unsafe fixed byte Reserved[18];
        public ushort GroupCount;
        public GROUP_AFFINITY GroupMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX_CACHE
    {
        public int Relationship;
        public uint Size;
        public CACHE_RELATIONSHIP Cache;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemCpuSetInformation(nint Information, uint BufferLength, ref uint ReturnedLength, nint Process, uint Flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetLogicalProcessorInformationEx(int RelationshipType, nint Buffer, ref uint ReturnedLength);

    public static IEnumerable<AcpuCpuSet> GetCpuSets()
    {
        uint len = 0;
        GetSystemCpuSetInformation(nint.Zero, 0, ref len, nint.Zero, 0);
        if (len == 0) yield break;

        nint buffer = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetSystemCpuSetInformation(buffer, len, ref len, nint.Zero, 0))
                yield break;

            nint current = buffer;
            nint end = buffer + (int)len;
            while ((long)current < (long)end)
            {
                var info = Marshal.PtrToStructure<SystemCpuSetInformation>(current);
                if (info.Size == 0) break;
                yield return new AcpuCpuSet(
                    info.Id,
                    info.Group,
                    info.LogicalProcessorIndex,
                    info.CoreIndex,
                    info.LastLevelCacheIndex,
                    info.NumaNodeIndex,
                    info.EfficiencyClass);
                current += (int)info.Size;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static List<AcpuL3Group> GetL3CacheGroups()
    {
        var list = new List<AcpuL3Group>();
        uint len = 0;
        GetLogicalProcessorInformationEx(RelationCache, nint.Zero, ref len);
        if (len == 0) return list;

        nint buffer = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationCache, buffer, ref len))
                return list;

            nint current = buffer;
            nint end = buffer + (int)len;
            while ((long)current < (long)end)
            {
                int relationship = Marshal.ReadInt32(current);
                uint size = (uint)Marshal.ReadInt32(current + 4);
                if (size == 0) break;

                if (relationship == RelationCache)
                {
                    var item = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX_CACHE>(current);
                    if (item.Cache.Level == 3)
                    {
                        list.Add(new AcpuL3Group(
                            item.Cache.GroupMask.Group,
                            item.Cache.CacheSize,
                            item.Cache.Type,
                            item.Cache.LineSize,
                            item.Cache.Associativity,
                            item.Cache.GroupMask.Mask));
                    }
                }
                current += (int)size;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return list;
    }

    public static IEnumerable<int> GetLogicalProcessors(nuint mask)
    {
        int bits = nint.Size * 8;
        for (int i = 0; i < bits; i++)
        {
            if ((mask & ((nuint)1 << i)) != 0)
                yield return i;
        }
    }
}
