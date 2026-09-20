using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AgentMeter;

// UpdateLayeredWindow is the supported Win32 per-pixel-alpha path. Constant form
// opacity would also fade the text; this small premultiplied bitmap does not.
internal static class LayeredWindowSurface
{
    internal static void Present(nint window, Point position, Bitmap bitmap, byte opacity = 255)
    {
        var dc = CreateCompatibleDC(0);
        if (dc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint dib = 0, previous = 0;
        try
        {
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = bitmap.Width,
                    Height = -bitmap.Height, Planes = 1, BitCount = 32
                }
            };
            dib = CreateDIBSection(dc, ref info, 0, out var pixels, 0, 0);
            if (dib == 0 || pixels == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var row = new byte[checked(bitmap.Width * 4)];
                for (var y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    Marshal.Copy(row, 0, pixels + y * row.Length, row.Length);
                }
            }
            finally { bitmap.UnlockBits(data); }
            previous = SelectObject(dc, dib);
            if (previous == 0 || previous == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var destination = new NativePoint(position.X, position.Y);
            var source = new NativePoint(0, 0);
            var blend = new BlendFunction { SourceConstantAlpha = opacity, AlphaFormat = 1 };
            if (!UpdateLayeredWindow(window, 0, ref destination, ref size, dc, ref source, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            if (previous != 0 && previous != -1) _ = SelectObject(dc, previous);
            if (dib != 0) _ = DeleteObject(dib);
            _ = DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativeSize(int Width, int Height);
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }

    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(nint window, nint screenDc, ref NativePoint position,
        ref NativeSize size, nint sourceDc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
}
