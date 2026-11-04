namespace Clothesline;

internal sealed class MonitorDesc
{
    public required IntPtr Handle { get; init; }
    public required NativeMethods.RECT Bounds { get; init; }
    public required uint DpiX { get; init; }
    public required string Device { get; init; }

    public double Scale => Math.Max(1, DpiX) / 96.0;

    public bool Contains(int x, int y) => Bounds.Contains(x, y);

    public bool InTopBand(int x, int y)
    {
        if (x < Bounds.Left || x >= Bounds.Right) return false;
        var band = Math.Max(16, (int)Math.Round(Layout.TopBandDip * Scale));
        return y >= Bounds.Top && y < Bounds.Top + band;
    }
}

internal static class Monitors
{
    public static IReadOnlyList<MonitorDesc> All()
    {
        var list = new List<MonitorDesc>();
        NativeMethods.MonitorEnumProc proc = (IntPtr hmon, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (!NativeMethods.GetMonitorInfo(hmon, ref info)) return true;
            uint dpiX = 96, dpiY = 96;
            NativeMethods.GetDpiForMonitor(hmon, 0, out dpiX, out dpiY);
            if (dpiX < 96) dpiX = 96;
            list.Add(new MonitorDesc
            {
                Handle = hmon,
                Bounds = info.rcMonitor,
                DpiX = dpiX,
                Device = string.IsNullOrEmpty(info.szDevice) ? hmon.ToString() : info.szDevice,
            });
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        return list;
    }

    public static MonitorDesc? Under(IReadOnlyList<MonitorDesc> all, int x, int y)
    {
        var hit = all.FirstOrDefault(m => m.Contains(x, y));
        if (hit != null) return hit;
        var pt = new NativeMethods.POINT { X = x, Y = y };
        var h = NativeMethods.MonitorFromPoint(pt, NativeMethods.MonitorDefaultToNearest);
        return all.FirstOrDefault(m => m.Handle == h) ?? all.FirstOrDefault();
    }
}
