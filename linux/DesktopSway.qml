import QtQuick
import Quickshell.I3

// Desktop.qml 的 Sway 后端，只在 Sway 会话里加载。聚焦屏走 Sway IPC（空工作区也认得出）；
// 全屏判断没写在这里，由 Desktop.qml 走 toplevel 协议。
QtObject {
    function focusedName() { return I3.focusedMonitor ? I3.focusedMonitor.name : ""; }
}
