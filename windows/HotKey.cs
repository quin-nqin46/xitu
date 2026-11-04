// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Clothesline;

/// 多个全局热键。换组合失败时，原来的那个还在。
internal sealed class HotKey : IDisposable
{
    public const int CaptureId = 1;
    public const int ToggleId = 2;
    public const int GalleryId = 3;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Slot> _slots = new();

    public HotKey()
    {
        var parameters = new HwndSourceParameters("Clothesline.Hotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = NativeMethods.HwndMessage,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(Hook);
    }

    public void Clear(int id)
    {
        if (!_slots.Remove(id)) return;
        NativeMethods.UnregisterHotKey(_source.Handle, id);
        Log.Info("已取消热键 id " + id);
    }

    /// 立刻换成新组合。失败时重新登记原来的组合，并返回 false。
    public bool TryReplace(int id, HotkeyChord chord, Action action, string name)
    {
        if (!chord.IsBindable)
        {
            Log.Error("热键无效 " + name);
            return false;
        }

        _slots.TryGetValue(id, out var previous);
        if (previous != null) NativeMethods.UnregisterHotKey(_source.Handle, id);

        if (!Register(id, chord))
        {
            var error = Marshal.GetLastWin32Error();
            if (previous != null && !Register(id, previous.Chord))
                Log.Error("原热键也登记不回去 " + name);
            Log.Error($"RegisterHotKey 失败 {name} {chord.Format()}，错误码 {error}");
            return false;
        }

        _slots[id] = new Slot(chord, action, name);
        Log.Info($"已注册热键 {name} {chord.Format()}");
        return true;
    }

    public void Dispose()
    {
        foreach (var id in _slots.Keys)
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _slots.Clear();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }

    private bool Register(int id, HotkeyChord chord)
    {
        var mods = chord.Modifiers | NativeMethods.ModNoRepeat;
        return NativeMethods.RegisterHotKey(_source.Handle, id, mods, chord.Key);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && _slots.TryGetValue(wParam.ToInt32(), out var slot))
        {
            handled = true;
            slot.Action();
        }
        return IntPtr.Zero;
    }

    private sealed record Slot(HotkeyChord Chord, Action Action, string Name);
}
