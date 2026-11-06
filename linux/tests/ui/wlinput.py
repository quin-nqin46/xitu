"""纯 Python 的 Wayland 虚拟鼠标（zwlr_virtual_pointer_v1），给界面测试用。

不依赖 pywayland，直接说 Wayland 的线协议：连上合成器的 socket，绑定
zwlr_virtual_pointer_manager_v1，建一个虚拟鼠标，然后发绝对坐标移动和按键。
Sway、Hyprland 都支持这个协议。合成器里有了这个设备，座位才有「鼠标」能力，
Qt 才会收到悬停、点击。

用法（在测试脚本里）：
    p = VirtualPointer(1920, 1080)   # 整个输出布局的大小
    p.move(960, 1)
    p.click(960, 120, button="right")
"""

import os
import socket
import struct
import time

BTN = {"left": 0x110, "right": 0x111, "middle": 0x112}


def _pad(n):
    return (n + 3) & ~3


def _string(s):
    b = s.encode() + b"\0"
    return struct.pack("<I", len(b)) + b + b"\0" * (_pad(len(b)) - len(b))


class WaylandError(RuntimeError):
    pass


class VirtualPointer:
    def __init__(self, width, height, display=None, runtime_dir=None):
        self.width = int(width)
        self.height = int(height)
        display = display or os.environ.get("WAYLAND_DISPLAY", "wayland-0")
        runtime_dir = runtime_dir or os.environ["XDG_RUNTIME_DIR"]
        path = display if display.startswith("/") else os.path.join(runtime_dir, display)
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.connect(path)
        self.buf = b""
        self.next_id = 2  # 1 是 wl_display
        self.globals = {}
        self.t0 = time.monotonic()
        self.x = 0
        self.y = 0

        self.registry = self._new_id()
        self._send(1, 1, struct.pack("<I", self.registry))  # wl_display.get_registry
        self.roundtrip()
        mgr = self.globals.get("zwlr_virtual_pointer_manager_v1")
        if not mgr:
            raise WaylandError("合成器不支持 zwlr_virtual_pointer_manager_v1")
        name, _ver = mgr
        self.manager = self._new_id()
        # wl_registry.bind(name, interface, version, new_id)
        self._send(self.registry, 0, struct.pack("<I", name)
                   + _string("zwlr_virtual_pointer_manager_v1")
                   + struct.pack("<II", 1, self.manager))
        self.pointer = self._new_id()
        # create_virtual_pointer(seat = null → 默认座位, id)
        self._send(self.manager, 0, struct.pack("<II", 0, self.pointer))
        self.roundtrip()

    # ---- 线协议 ----
    def _new_id(self):
        i = self.next_id
        self.next_id += 1
        return i

    def _send(self, obj, opcode, payload=b""):
        size = 8 + len(payload)
        self.sock.sendall(struct.pack("<II", obj, (size << 16) | opcode) + payload)

    def _read_events(self):
        chunk = self.sock.recv(65536)
        if not chunk:
            raise WaylandError("合成器断开了连接")
        self.buf += chunk
        events = []
        while len(self.buf) >= 8:
            obj, word = struct.unpack_from("<II", self.buf)
            size = word >> 16
            if len(self.buf) < size:
                break
            events.append((obj, word & 0xFFFF, self.buf[8:size]))
            self.buf = self.buf[size:]
        return events

    def roundtrip(self):
        cb = self._new_id()
        self._send(1, 0, struct.pack("<I", cb))  # wl_display.sync
        while True:
            for obj, op, body in self._read_events():
                if obj == 1 and op == 0:  # wl_display.error
                    oid, code, n = struct.unpack_from("<III", body)
                    msg = body[12:12 + n - 1].decode(errors="replace")
                    raise WaylandError(f"协议错误 object={oid} code={code}: {msg}")
                if obj == self.registry and op == 0:  # wl_registry.global
                    name, n = struct.unpack_from("<II", body)
                    iface = body[8:8 + n - 1].decode()
                    ver = struct.unpack_from("<I", body, 8 + _pad(n))[0]
                    self.globals[iface] = (name, ver)
                if obj == cb and op == 0:
                    return

    def _ms(self):
        return int((time.monotonic() - self.t0) * 1000) & 0xFFFFFFFF

    def _frame(self):
        self._send(self.pointer, 4)  # frame

    # ---- 动作 ----
    def move(self, x, y):
        self.x, self.y = int(x), int(y)
        xx = max(0, min(self.width, self.x))
        yy = max(0, min(self.height, self.y))
        # motion_absolute(time, x, y, x_extent, y_extent)
        self._send(self.pointer, 1, struct.pack("<IIIII", self._ms(), xx, yy, self.width, self.height))
        self._frame()
        self.roundtrip()

    def glide(self, x, y, steps=12, pause=0.012):
        """分几步移过去（拖动、悬停时 Qt 要看到中间的移动事件）。"""
        x0, y0 = self.x, self.y
        for i in range(1, steps + 1):
            self.move(x0 + (x - x0) * i / steps, y0 + (y - y0) * i / steps)
            time.sleep(pause)

    def button(self, button, pressed):
        code = BTN.get(button, button)
        self._send(self.pointer, 2, struct.pack("<III", self._ms(), code, 1 if pressed else 0))
        self._frame()
        self.roundtrip()

    def click(self, x=None, y=None, button="left", hold=0.05):
        if x is not None:
            self.move(x, y)
            time.sleep(0.05)
        self.button(button, True)
        time.sleep(hold)
        self.button(button, False)

    def drag(self, x1, y1, x2, y2, steps=20, pause=0.02):
        self.move(x1, y1)
        time.sleep(0.1)
        self.button("left", True)
        time.sleep(0.1)
        self.glide(x2, y2, steps=steps, pause=pause)
        time.sleep(0.2)
        self.button("left", False)

    def close(self):
        try:
            self._send(self.pointer, 8)  # destroy
            self.roundtrip()
        finally:
            self.sock.close()
