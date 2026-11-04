// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.IO;
using System.Text.Json;

namespace Clothesline;

/// 一个可以注册的组合键。修饰键为 0 时只允许 PrtSc。
internal readonly record struct HotkeyChord(uint Modifiers, uint Key)
{
    public static HotkeyChord CaptureDefault => new(NativeMethods.ModControl | NativeMethods.ModShift, 0x41);
    public static HotkeyChord ToggleDefault => new(NativeMethods.ModControl | NativeMethods.ModAlt, NativeMethods.VkT);

    public bool IsBindable
    {
        get
        {
            if (Key == 0 || IsModifier(Key)) return false;
            if (Key == NativeMethods.VkSnapshot) return true;
            return Modifiers != 0;
        }
    }

    public string Format()
    {
        if (Key == NativeMethods.VkSnapshot && Modifiers == 0) return "PrtSc";
        var parts = new List<string>();
        if ((Modifiers & NativeMethods.ModControl) != 0) parts.Add("Ctrl");
        if ((Modifiers & NativeMethods.ModAlt) != 0) parts.Add("Alt");
        if ((Modifiers & NativeMethods.ModShift) != 0) parts.Add("Shift");
        if ((Modifiers & NativeMethods.ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    public static bool IsModifier(uint vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private static string KeyName(uint vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        return vk switch
        {
            0x2C => "PrtSc",
            0x0D => "Enter",
            0x20 => "Space",
            0x09 => "Tab",
            0x1B => "Esc",
            0x2E => "Delete",
            0x08 => "Backspace",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            _ => "VK" + vk.ToString("X"),
        };
    }
}

/// 快捷键和截图选项。以后完整设置页也走这里。
internal sealed class AppSettings
{
    public HotkeyChord Capture { get; set; } = HotkeyChord.CaptureDefault;
    public HotkeyChord ToggleLine { get; set; } = HotkeyChord.ToggleDefault;
    public HotkeyChord Gallery { get; set; } = default;
    public bool CopyToClipboard { get; set; } = true;
    public string LibraryPath { get; set; } = "";
    public int RetentionDays { get; set; } = 30;
    public double GalleryLeft { get; set; } = double.NaN;
    public double GalleryTop { get; set; } = double.NaN;
    public double GalleryWidth { get; set; } = 0;
    public double GalleryHeight { get; set; } = 0;

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return;
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(AppPaths.SettingsFile));
            if (dto == null) return;
            var capture = new HotkeyChord(dto.CaptureModifiers, dto.CaptureKey);
            var toggle = new HotkeyChord(dto.ToggleModifiers, dto.ToggleKey);
            var gallery = new HotkeyChord(dto.GalleryModifiers, dto.GalleryKey);
            Current = new AppSettings
            {
                Capture = capture.IsBindable ? capture : HotkeyChord.CaptureDefault,
                ToggleLine = toggle.IsBindable ? toggle : HotkeyChord.ToggleDefault,
                Gallery = gallery.IsBindable ? gallery : default,
                CopyToClipboard = dto.CopyToClipboard,
                LibraryPath = dto.LibraryPath ?? "",
                RetentionDays = dto.RetentionDays ?? 30,
                GalleryLeft = dto.GalleryLeft ?? double.NaN,
                GalleryTop = dto.GalleryTop ?? double.NaN,
                GalleryWidth = dto.GalleryWidth ?? 0,
                GalleryHeight = dto.GalleryHeight ?? 0,
            };
        }
        catch (Exception ex)
        {
            Log.Error("读取 settings.json 失败: " + ex.Message);
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var dto = new Dto
            {
                CaptureModifiers = Capture.Modifiers,
                CaptureKey = Capture.Key,
                ToggleModifiers = ToggleLine.Modifiers,
                ToggleKey = ToggleLine.Key,
                GalleryModifiers = Gallery.Modifiers,
                GalleryKey = Gallery.Key,
                CopyToClipboard = CopyToClipboard,
                LibraryPath = LibraryPath,
                RetentionDays = RetentionDays,
                GalleryLeft = GalleryLeft,
                GalleryTop = GalleryTop,
                GalleryWidth = GalleryWidth,
                GalleryHeight = GalleryHeight,
            };
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("写入 settings.json 失败: " + ex.Message);
        }
    }

    private sealed class Dto
    {
        public uint CaptureModifiers { get; set; }
        public uint CaptureKey { get; set; }
        public uint ToggleModifiers { get; set; }
        public uint ToggleKey { get; set; }
        public uint GalleryModifiers { get; set; }
        public uint GalleryKey { get; set; }
        public bool CopyToClipboard { get; set; } = true;
        public string? LibraryPath { get; set; }
        public int? RetentionDays { get; set; }
        public double? GalleryLeft { get; set; }
        public double? GalleryTop { get; set; }
        public double? GalleryWidth { get; set; }
        public double? GalleryHeight { get; set; }
    }
}
