<#
.SYNOPSIS
    Schiesst einen echten Windows-Screenshot vom Hauptfenster eines laufenden
    Prozesses und speichert ihn als PNG. Fuer die visuelle Pruefung einer
    laufenden Referenz-App (siehe ../SKILL.md).

.PARAMETER ProcessName
    Name des Prozesses (ohne .exe), dessen Hauptfenster fotografiert werden
    soll, z. B. "AvaloniaApplication1".

.PARAMETER OutFile
    Zielpfad fuer die PNG-Datei.

.NOTES
    Holt das Zielfenster per SetForegroundWindow in den Vordergrund - das
    reisst den Fokus von der aktuell aktiven Anwendung weg. Vorher beim User
    nachfragen, falls dieser gerade in einer Vollbild-Anwendung arbeiten
    koennte (Spiel, Praesentation, Video).

.EXAMPLE
    powershell -File Take-Screenshot.ps1 -ProcessName AvaloniaApplication1 -OutFile shot1.png
#>
param(
    [Parameter(Mandatory = $true)][string]$ProcessName,
    [Parameter(Mandatory = $true)][string]$OutFile
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Screenshot {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) {
    throw "Kein laufender Prozess '$ProcessName' mit sichtbarem Hauptfenster gefunden."
}

[Win32Screenshot]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 200

$rect = New-Object Win32Screenshot+RECT
[Win32Screenshot]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null

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
