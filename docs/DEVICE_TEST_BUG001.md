# BUG-001: external display disconnect

Reproduced on preview.10 using a portrait secondary display and BitBlt: unplugging the display moved the topmost output onto the primary screen and obstructed control.

The native output now latches loss or geometry/identity changes of the selected endpoint, hides the window and removes topmost status immediately. A window-position guard and connection check also prevent continuing on a disappeared target. Composition continues without presenting to the failed output. Managed code detaches only external presentation and reports the change; input, audio and the shared playback canvas remain alive. Reconnection never automatically starts output. A change that invalidates the selected endpoint requires explicit output restart; unchanged display notifications are ignored.

The rendering-health regression injects the real WM_DISPLAYCHANGE path on probe windows in both Flip and BitBlt and checks hiding, detachment, no auto-reattach, continuing video and the same audio sink. This does not replace the physical unplug/replug retest.

Device testing on the original PC covered static disconnect/reconnect in both presentation modes and active playback in BitBlt. Controls remained accessible and explicit output restart was possible. The user heard a temporary LG audio interruption followed by resumption; this is not a seamless-audio guarantee. Repeat on the destination PC. Current handoff and remaining work: [HANDOFF_2026-11-13.md](HANDOFF_2026-11-13.md).
