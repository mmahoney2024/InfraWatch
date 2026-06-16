using System.Runtime.InteropServices;

namespace InfraWatch.Collectors.Imaging;

/// <summary>Free-space lookup that works on UNC paths (DriveInfo does not). Wraps the Win32
/// <c>GetDiskFreeSpaceEx</c> API, which accepts a directory or share path.</summary>
internal static class DiskFree
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    public static bool TryGet(string path, out double freeGb, out double totalGb, out double freePct)
    {
        freeGb = totalGb = freePct = 0;
        try
        {
            if (!GetDiskFreeSpaceExW(path, out _, out var total, out var totalFree) || total == 0)
                return false;
            const double gb = 1024.0 * 1024.0 * 1024.0;
            totalGb = total / gb;
            freeGb = totalFree / gb;
            freePct = (double)totalFree / total * 100.0;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
