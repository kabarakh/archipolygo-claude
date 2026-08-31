#!/usr/bin/env bash
#
# macOS-Gegenstueck zu Take-Screenshot.ps1: schiesst einen Screenshot von
# genau einem Fenster eines laufenden Prozesses und speichert ihn als PNG.
# Siehe ../SKILL.md, Abschnitt "macOS-Variante".
#
# Benoetigt einmalig erteilte Rechte fuer den Prozess, der dieses Skript
# ausfuehrt (Terminal/iTerm/o.ae.), unter Systemeinstellungen ->
# Datenschutz & Sicherheit:
#   - Bedienungshilfen (fuer die Fensterabfrage per System Events)
#   - Bildschirmaufnahme (fuer screencapture)
# Ohne "Bedienungshilfen" schlaegt die Fensterabfrage mit Fehler -1719 fehl.
#
# Dieses Skript selbst braucht kein cliclick (nur Fensterabfrage +
# screencapture) - das gemeinsam genutzte Send-Click.sh daneben aber schon;
# siehe dessen Kopfkommentar und ../SKILL.md, Abschnitt "macOS-Variante".
#
# Usage:
#   Take-Screenshot.sh -p ProcessName -o OutFile.png [-t TitleSubstring]
#
# -p ProcessName     Name des Prozesses, dessen Fenster fotografiert werden
#                     soll, z. B. "AvaloniaApplication1" (wie im Activity
#                     Monitor/`ps`, nicht der App-Bundle-Name).
# -o OutFile         Zielpfad fuer die PNG-Datei.
# -t TitleSubstring  Optional, wie beim PowerShell-Pendant: waehlt bei
#                     mehreren sichtbaren Fenstern des Prozesses (z. B.
#                     Hauptfenster + ControlPanelWindow) gezielt das Fenster,
#                     dessen Titel diesen Teilstring enthaelt, statt einfach
#                     "window 1" zu nehmen.
#
# Beispiel:
#   .claude/skills/app-testen/scripts/Take-Screenshot.sh \
#       -p AvaloniaApplication1 -o shot1.png
#
# Holt das Zielfenster per "set frontmost" in den Vordergrund - das reisst
# den Fokus von der aktuell aktiven Anwendung weg. Siehe Grundprinzip 4 in
# ../SKILL.md: vorher beim User nachfragen, falls der gerade in einer
# Vollbild-Anwendung arbeiten koennte.
#
# NIEMALS stattdessen den gesamten Bildschirm (alle Monitore) fotografieren,
# um dieser Mehrfenster-Problematik auszuweichen - siehe "Niemals
# Vollbild-Screenshots" in ../SKILL.md.

set -euo pipefail

PROCESS_NAME=""
OUT_FILE=""
TITLE_CONTAINS=""

while getopts "p:o:t:" opt; do
    case "$opt" in
        p) PROCESS_NAME="$OPTARG" ;;
        o) OUT_FILE="$OPTARG" ;;
        t) TITLE_CONTAINS="$OPTARG" ;;
        *)
            echo "Usage: $0 -p ProcessName -o OutFile.png [-t TitleSubstring]" >&2
            exit 1
            ;;
    esac
done

if [[ -z "$PROCESS_NAME" || -z "$OUT_FILE" ]]; then
    echo "Usage: $0 -p ProcessName -o OutFile.png [-t TitleSubstring]" >&2
    exit 1
fi

# System Events adressiert Prozesse ueber ihren sichtbaren Namen (wie im
# Dock/Activity Monitor). Fenstertitel-Filterung passiert direkt in
# AppleScript, analog zum EnumWindows-Callback im PowerShell-Skript.
BOUNDS=$(osascript <<APPLESCRIPT
tell application "System Events"
    if not (exists process "$PROCESS_NAME") then
        error "Kein laufender Prozess '$PROCESS_NAME' gefunden."
    end if
    tell process "$PROCESS_NAME"
        set targetWindow to missing value
        if "$TITLE_CONTAINS" is not "" then
            repeat with w in windows
                if (name of w contains "$TITLE_CONTAINS") then
                    set targetWindow to w
                    exit repeat
                end if
            end repeat
            if targetWindow is missing value then
                error "Kein sichtbares Fenster von '$PROCESS_NAME' mit Titel-Teilstring '$TITLE_CONTAINS' gefunden."
            end if
        else
            if (count of windows) is 0 then
                error "Prozess '$PROCESS_NAME' hat kein sichtbares Fenster."
            end if
            set targetWindow to window 1
        end if
        set frontmost to true
        delay 0.2
        set {posX, posY} to position of targetWindow
        set {winW, winH} to size of targetWindow
        return (posX as string) & "," & (posY as string) & "," & (winW as string) & "," & (winH as string)
    end tell
end tell
APPLESCRIPT
)

IFS=',' read -r X Y WIDTH HEIGHT <<< "$BOUNDS"

if [[ -z "${WIDTH:-}" || -z "${HEIGHT:-}" || "$WIDTH" -le 0 || "$HEIGHT" -le 0 ]]; then
    echo "Ungueltige Fenstergroesse ermittelt (${WIDTH:-?} x ${HEIGHT:-?}). Ist das Fenster minimiert?" >&2
    exit 1
fi

# -x unterdrueckt den Verschlussgeraeusch; -R nimmt genau den Fensterbereich
# auf statt des ganzen Bildschirms.
screencapture -x -R "${X},${Y},${WIDTH},${HEIGHT}" "$OUT_FILE"

echo "Screenshot gespeichert: $OUT_FILE (Fensterposition: ${X},${Y}, Groesse: ${WIDTH}x${HEIGHT})"
