import QtQuick
import QtQuick.Effects
import QtQuick.Shapes
import "layout.js" as L

// 绳子、阴影、麻绳纹，两端按主题画圆钉或菱形结加流苏。
Item {
    id: rope

    required property var theme

    readonly property real sagNow: L.sag(Math.max(width, 1))
    readonly property real x0: theme.endStyle === "pin" ? theme.endInset : 0
    readonly property real x1: theme.endStyle === "pin" ? Math.max(x0, width - theme.endInset) : width
    readonly property real y0: L.ropeY(x0, Math.max(width, 1))
    readonly property real y1: L.ropeY(x1, Math.max(width, 1))
    // 二次曲线在中点的高度要落在 ropeY 上：中点 = (两端 + 2×控制点) / 4
    readonly property real ctrlY: 2 * L.ropeY(width / 2, Math.max(width, 1)) - 0.5 * (y0 + y1)

    Shape {
        anchors.fill: parent
        preferredRendererType: Shape.CurveRenderer

        ShapePath {
            strokeColor: rope.theme.ropeShadow
            strokeWidth: rope.theme.ropeShadowWidth
            fillColor: "transparent"
            capStyle: ShapePath.RoundCap
            startX: rope.x0
            startY: rope.y0 + rope.theme.ropeShadowOffset
            PathQuad {
                x: rope.x1
                y: rope.y1 + rope.theme.ropeShadowOffset
                controlX: rope.width / 2
                controlY: rope.ctrlY + rope.theme.ropeShadowOffset
            }
        }

        ShapePath {
            strokeColor: rope.theme.ropeColor
            strokeWidth: rope.theme.ropeWidth
            fillColor: "transparent"
            capStyle: ShapePath.RoundCap
            startX: rope.x0
            startY: rope.y0
            PathQuad {
                x: rope.x1
                y: rope.y1
                controlX: rope.width / 2
                controlY: rope.ctrlY
            }
        }

        // twist：白色半透明虚线，3 像素一段、空 5 像素。dashPattern 的单位是线宽。
        ShapePath {
            strokeColor: rope.theme.ropeTexture === "twist" ? Qt.rgba(1, 1, 1, 0.35) : "transparent"
            strokeWidth: rope.theme.ropeWidth
            fillColor: "transparent"
            capStyle: ShapePath.RoundCap
            strokeStyle: ShapePath.DashLine
            dashPattern: [3 / Math.max(rope.theme.ropeWidth, 0.1), 5 / Math.max(rope.theme.ropeWidth, 0.1)]
            startX: rope.x0
            startY: rope.y0
            PathQuad {
                x: rope.x1
                y: rope.y1
                controlX: rope.width / 2
                controlY: rope.ctrlY
            }
        }
    }

    Repeater {
        model: rope.theme.endStyle === "none" ? [] : [rope.theme.endInset, Math.max(0, rope.width - rope.theme.endInset)]

        delegate: Item {
            id: tip
            required property real modelData
            x: modelData
            y: L.ropeY(modelData, rope.width)
            visible: modelData >= 8 && modelData <= rope.width - 8

            Item {
                visible: rope.theme.endStyle === "pin"
                width: rope.theme.endRadius * 2
                height: width
                x: -width / 2
                y: -height / 2

                RectangularShadow {
                    anchors.fill: pinBody
                    offset.y: 2
                    blur: 5
                    radius: pinBody.radius
                    color: Qt.rgba(27 / 255, 36 / 255, 64 / 255, 0.25)
                }

                Rectangle {
                    id: pinBody
                    anchors.fill: parent
                    radius: width / 2
                    color: rope.theme.pinColor
                    border.width: 1
                    border.color: rope.theme.pinRing
                    antialiasing: true
                }

                Rectangle {
                    anchors.centerIn: parent
                    width: rope.theme.dotRadius * 2
                    height: width
                    radius: width / 2
                    color: rope.theme.dotColor
                    antialiasing: true
                }
            }

            // 菱形结和流苏（red-rope）。结的边长是 ends.radius，流苏按 14 → 3×26 的比例。
            Item {
                id: knot
                visible: rope.theme.endStyle === "knot"
                readonly property real side: rope.theme.endRadius

                Rectangle {
                    width: knot.side
                    height: knot.side
                    x: -width / 2
                    y: -height / 2
                    radius: knot.side / 7
                    rotation: 45
                    color: rope.theme.ropeColor
                }

                Rectangle {
                    width: knot.side * 3 / 14
                    height: knot.side * 26 / 14
                    x: -width / 2
                    y: knot.side / 2 * Math.SQRT2
                    color: rope.theme.ropeColor
                }
            }
        }
    }
}
