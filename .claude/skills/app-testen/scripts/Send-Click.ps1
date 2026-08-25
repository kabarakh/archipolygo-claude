<#
.SYNOPSIS
    Bewegt den echten Mauszeiger an Bildschirmkoordinaten und loest einen
    Linksklick (oder ein Mausrad-Scrollen) aus - wie ein Benutzer es taete.
    Fuer die visuelle Interaktion mit einer laufenden Referenz-App (siehe
    ../SKILL.md).

.PARAMETER X
    Bildschirm-X-Koordinate (absolut, wie im vorherigen Screenshot abgelesen).

.PARAMETER Y
    Bildschirm-Y-Koordinate (absolut, wie im vorherigen Screenshot abgelesen).

.PARAMETER Scroll
    Optional: statt eines Klicks stattdessen scrollen. Positive Werte
    scrollen nach oben, negative nach unten (typisch: -3 fuer "ein Stueck
    runter scrollen").

.PARAMETER DoubleClick
    Optional: Doppelklick statt einfachem Klick.

.EXAMPLE
    powershell -File Send-Click.ps1 -X 640 -Y 420

.EXAMPLE
    powershell -File Send-Click.ps1 -X 640 -Y 420 -Scroll -5
#>
param(
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y,
    [int]$Scroll = 0,
    [switch]$DoubleClick
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Input {
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    // dwData is `int`, not the Win32 header's `uint` - a negative wheel
    // delta (scroll down) needs to reach mouse_event with the same 4-byte
    // bit pattern either way, and PowerShell's `[uint32]` cast throws on a
    // negative source value instead of reinterpreting the bits. `int`
    // marshals identically for the native call and never throws.
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, IntPtr dwExtraInfo);

    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
}
"@

[Win32Input]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 100

if ($Scroll -ne 0) {
    # WHEEL_DELTA ist 120 pro "Rasterschritt".
    [Win32Input]::mouse_event([Win32Input]::MOUSEEVENTF_WHEEL, 0, 0, ($Scroll * 120), [IntPtr]::Zero)
    Write-Output "Gescrollt bei ($X,$Y) um $Scroll Schritte."
    return
}

function Do-Click {
    [Win32Input]::mouse_event([Win32Input]::MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 50
    [Win32Input]::mouse_event([Win32Input]::MOUSEEVENTF_LEFTUP, 0, 0, 0, [IntPtr]::Zero)
}

Do-Click
if ($DoubleClick) {
    Start-Sleep -Milliseconds 80
    Do-Click
}

Write-Output "Geklickt bei ($X,$Y)$(if ($DoubleClick) { ' (Doppelklick)' })."
