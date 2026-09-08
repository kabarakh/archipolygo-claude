<#
.SYNOPSIS
    Schiesst einen echten Windows-Screenshot von genau einem Fenster eines
    laufenden Prozesses und speichert ihn als PNG. Fuer die visuelle Pruefung
    einer laufenden Referenz-App (siehe ../SKILL.md).

.PARAMETER ProcessName
    Name des Prozesses (ohne .exe), dessen Fenster fotografiert werden soll,
    z. B. "AvaloniaApplication1".

.PARAMETER OutFile
    Zielpfad fuer die PNG-Datei.

.PARAMETER TitleContains
    Optional. Wenn der Prozess mehr als ein sichtbares Top-Level-Fenster hat
    (z. B. Hauptfenster + separates ControlPanelWindow + dynamisch erzeugte
    weitere Fenster), waehlt dieser Titel-Teilstring das gemeinte Fenster
    gezielt aus, statt sich auf `MainWindowHandle` zu verlassen - das liefert
    pro Prozess nur GENAU EIN Handle nach einer eigenen Windows-Heuristik
    ("welches Fenster gilt als das Hauptfenster"), das nicht zwingend das
    Fenster ist, das gerade fotografiert werden soll. Ohne diesen Parameter
    bleibt das bisherige Verhalten (MainWindowHandle) unveraendert.

.NOTES
    Holt das Zielfenster per SetForegroundWindow in den Vordergrund - das
    reisst den Fokus von der aktuell aktiven Anwendung weg. Vorher beim User
    nachfragen, falls dieser gerade in einer Vollbild-Anwendung arbeiten
    koennte (Spiel, Praesentation, Video).

    NIEMALS stattdessen einen Screenshot des gesamten virtuellen Bildschirms
    (alle Monitore) machen, um dieser Mehrfenster-Problematik auszuweichen -
    siehe "Niemals Vollbild-Screenshots" in ../SKILL.md fuer den Vorfall, der
    diesen Parameter ausgeloest hat.

.EXAMPLE
    powershell -File Take-Screenshot.ps1 -ProcessName AvaloniaApplication1 -OutFile shot1.png

.EXAMPLE
    powershell -File Take-Screenshot.ps1 -ProcessName TestHarness -OutFile panel.png -TitleContains "Test Control Panel"
#>
param(
    [Parameter(Mandatory = $true)][string]$ProcessName,
    [Parameter(Mandatory = $true)][string]$OutFile,
    [string]$TitleContains
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Win32Screenshot {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint procId);
}
"@

$targetHandle = [IntPtr]::Zero

if ($TitleContains) {
    # Enumerate every visible top-level window belonging to any process
    # named $ProcessName and take the first whose title contains
    # $TitleContains - see param doc above for why this exists instead of
    # just using MainWindowHandle.
    $procIds = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $callback = {
        param($hWnd, $lParam)
        $procId = 0
        [Win32Screenshot]::GetWindowThreadProcessId($hWnd, [ref]$procId) | Out-Null
        if (($procIds -contains [int]$procId) -and [Win32Screenshot]::IsWindowVisible($hWnd)) {
            $sb = New-Object System.Text.StringBuilder 256
            [Win32Screenshot]::GetWindowText($hWnd, $sb, 256) | Out-Null
            if ($sb.ToString() -like "*$TitleContains*") {
                $script:targetHandle = $hWnd
                return $false # found it, stop enumerating
            }
        }
        return $true
    }
    [Win32Screenshot]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null

    if ($targetHandle -eq [IntPtr]::Zero) {
        throw "Kein sichtbares Fenster von '$ProcessName' mit Titel-Teilstring '$TitleContains' gefunden."
    }
} else {
    $proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $proc) {
        throw "Kein laufender Prozess '$ProcessName' mit sichtbarem Hauptfenster gefunden."
    }
    $targetHandle = $proc.MainWindowHandle
}

[Win32Screenshot]::SetForegroundWindow($targetHandle) | Out-Null
Start-Sleep -Milliseconds 200

$rect = New-Object Win32Screenshot+RECT
[Win32Screenshot]::GetWindowRect($targetHandle, [ref]$rect) | Out-Null

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    throw "Ungueltige Fenstergroesse ermittelt ($width x $height). Ist das Fenster minimiert?"
}

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
$bitmap.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)

$graphics.Dispose()
$bitmap.Dispose()

Write-Output "Screenshot gespeichert: $OutFile (Fensterposition: $($rect.Left),$($rect.Top), Groesse: ${width}x${height})"
