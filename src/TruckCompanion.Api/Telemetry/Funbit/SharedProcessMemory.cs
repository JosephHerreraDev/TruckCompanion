using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// Derived from Funbit/ets2-telemetry-server, GPL-3.0.
// Original project: https://github.com/Funbit/ets2-telemetry-server
namespace TruckCompanion.Api.Telemetry.Funbit;

[SupportedOSPlatform("windows")]
internal sealed class SharedProcessMemory<T> : IDisposable
{
    private readonly string mapName;
    private MemoryMappedFile? memoryMappedFile;
    private MemoryMappedViewAccessor? memoryMappedAccessor;

    public SharedProcessMemory(string mapName)
    {
        this.mapName = mapName;
        Data = default!;
    }

    public T Data { get; set; }

    public bool IsConnected
    {
        get
        {
            InitializeViewAccessor();
            return memoryMappedAccessor is not null;
        }
    }

    public void Read()
    {
        InitializeViewAccessor();

        if (memoryMappedAccessor is null)
        {
            return;
        }

        var rawData = new byte[Marshal.SizeOf(typeof(T))];
        memoryMappedAccessor.ReadArray(0, rawData, 0, rawData.Length);

        var reservedMemPtr = IntPtr.Zero;
        try
        {
            reservedMemPtr = Marshal.AllocHGlobal(rawData.Length);
            Marshal.Copy(rawData, 0, reservedMemPtr, rawData.Length);
            Data = (T)Marshal.PtrToStructure(reservedMemPtr, typeof(T))!;
        }
        finally
        {
            if (reservedMemPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(reservedMemPtr);
            }
        }
    }

    public void Dispose()
    {
        memoryMappedAccessor?.Dispose();
        memoryMappedFile?.Dispose();
    }

    private void InitializeViewAccessor()
    {
        if (memoryMappedAccessor is not null)
        {
            return;
        }

        try
        {
            memoryMappedFile = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.ReadWrite);
            memoryMappedAccessor = memoryMappedFile.CreateViewAccessor(0, Marshal.SizeOf(typeof(T)), MemoryMappedFileAccess.Read);
        }
        catch
        {
            memoryMappedAccessor = null;
            memoryMappedFile?.Dispose();
            memoryMappedFile = null;
        }
    }
}
