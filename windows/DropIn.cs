// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.IO;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clothesline;

/// 从别处拖到绳上：图片文件复制进图库再挂；只有图像数据的（浏览器、聊天软件）直接写进图库。
internal static class DropIn
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };

    /// 外部拖拽正悬在某条绳子上。ScreenHost 据此不收起、不穿透。
    public static bool IsHovering { get; set; }

    public static bool Accepts(IDataObject data)
    {
        if (GrabArea.Busy) return false; // 自己拖出去的照片又经过绳子
        if (data.GetDataPresent(DataFormats.FileDrop))
            return ImageFiles(data).Count > 0;
        return data.GetDataPresent("PNG")
            || data.GetDataPresent(DataFormats.Bitmap)
            || data.GetDataPresent("FileGroupDescriptorW");
    }

    /// 返回挂上的张数。
    public static int Take(IDataObject data, Line line)
    {
        var hung = 0;
        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            foreach (var path in ImageFiles(data))
            {
                try
                {
                    var lib = Library.ImportFile(path, LibrarySource.File);
                    if (line.Hang(lib)) hung++;
                }
                catch (Exception ex) { Log.Error("拖入文件失败: " + ex.Message); }
            }
            return hung;
        }

        var bytes = ReadPng(data) ?? ReadFileContents(data) ?? ReadBitmap(data);
        if (bytes == null)
        {
            Log.Info("拖进来的内容里没有可用的图片，格式：" + string.Join(", ", data.GetFormats(false)));
            return 0;
        }
        try
        {
            var path = Library.ImportBytes(bytes, LibrarySource.Drop);
            if (line.Hang(path)) hung++;
        }
        catch (Exception ex)
        {
            Log.Error("拖入图片保存失败: " + ex.Message);
        }
        return hung;
    }

    private static List<string> ImageFiles(IDataObject data)
    {
        var result = new List<string>();
        if (data.GetData(DataFormats.FileDrop) is not string[] files) return result;
        foreach (var file in files)
        {
            if (File.Exists(file) && ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                result.Add(file);
        }
        return result;
    }

    private static byte[]? ReadPng(IDataObject data)
    {
        try
        {
            if (data.GetData("PNG") is MemoryStream png && png.Length > 0) return png.ToArray();
        }
        catch (Exception ex) { Log.Info("读取拖入 PNG 失败: " + ex.Message); }
        return null;
    }

    /// 浏览器拖图片常给虚拟文件：FileGroupDescriptorW 写文件名，FileContents 是内容。只取第一个。
    private static byte[]? ReadFileContents(IDataObject data)
    {
        try
        {
            if (data.GetData("FileGroupDescriptorW") is not MemoryStream descriptor || descriptor.Length < 4 + 76) return null;
            var raw = descriptor.ToArray();
            var name = Encoding.Unicode.GetString(raw, 4 + 72, Math.Min(520, raw.Length - 76)).Split('\0')[0];
            // 网页图片的虚拟文件名常不带后缀（如 X），不看后缀，能解码成图片就收。
            Log.Info("拖入虚拟文件 " + name);
            var content = ReadFileContentsAt(data, 0);
            if (content == null || content.Length == 0) return null;
            return Normalize(content);
        }
        catch (Exception ex)
        {
            Log.Info("读取拖入虚拟文件失败: " + ex.Message);
            return null;
        }
    }

    /// WPF 的 GetData 用 lindex = -1 取 FileContents，浏览器会拒绝（DV_E_FORMATETC）；这里直接走 COM，指定第 index 个文件。
    private static byte[]? ReadFileContentsAt(IDataObject data, int index)
    {
        if (data is not ComTypes.IDataObject com) return null;
        var format = new ComTypes.FORMATETC
        {
            cfFormat = (short)DataFormats.GetDataFormat("FileContents").Id,
            dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = index,
            ptd = IntPtr.Zero,
            tymed = ComTypes.TYMED.TYMED_ISTREAM | ComTypes.TYMED.TYMED_HGLOBAL,
        };
        com.GetData(ref format, out var medium);
        try
        {
            if (medium.tymed == ComTypes.TYMED.TYMED_ISTREAM)
            {
                var stream = (ComTypes.IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
                using var output = new MemoryStream();
                var buffer = new byte[64 * 1024];
                var readPtr = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    while (true)
                    {
                        stream.Read(buffer, buffer.Length, readPtr);
                        var read = Marshal.ReadInt32(readPtr);
                        if (read <= 0) break;
                        output.Write(buffer, 0, read);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(readPtr);
                    Marshal.ReleaseComObject(stream);
                }
                return output.ToArray();
            }
            if (medium.tymed == ComTypes.TYMED.TYMED_HGLOBAL)
            {
                var size = (int)GlobalSize(medium.unionmember);
                var ptr = GlobalLock(medium.unionmember);
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(ptr, bytes, 0, size);
                    return bytes;
                }
                finally { GlobalUnlock(medium.unionmember); }
            }
            return null;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr handle);

    private static byte[]? ReadBitmap(IDataObject data)
    {
        try
        {
            if (data.GetData(DataFormats.Bitmap) is BitmapSource bitmap) return Encode(bitmap);
        }
        catch (Exception ex) { Log.Info("读取拖入位图失败: " + ex.Message); }
        return null;
    }

    /// 虚拟文件可能是 jpg/webp/gif，统一转成 png 再进图库。
    private static byte[]? Normalize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        return decoder.Frames.Count == 0 ? null : Encode(decoder.Frames[0]);
    }

    private static byte[] Encode(BitmapSource source)
    {
        if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32 && source.Format != PixelFormats.Bgr32)
            source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
