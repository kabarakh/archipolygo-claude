#!/usr/bin/env bash
#
# macOS-Gegenstueck zu Send-Click.ps1: bewegt den echten Mauszeiger an
# Bildschirmkoordinaten und loest dort einen Linksklick aus - wie ein
# Benutzer es taete. Siehe ../SKILL.md, Abschnitt "macOS-Variante".
#
# VORAUSSETZUNG: cliclick (`brew install cliclick`). Dieses Skript nutzt es
# fuer die eigentliche Maussimulation, statt AppleScript/System Events'
# "click at" - ohne cliclick funktioniert das Skript nicht.
#
# Benoetigt zusaetzlich "Bedienungshilfen" (Accessibility) fuer den Prozess,
# der dieses Skript ausfuehrt (Terminal/iTerm/o.ae.) - unter
# Systemeinstellungen -> Datenschutz & Sicherheit -> Bedienungshilfen. cliclick
# meldet das selbst deutlich ("Accessibility privileges not enabled"), wenn
# das fehlt.
#
# Usage:
#   Send-Click.sh -x X -y Y [-d] [-s ScrollSteps]
#
# -x X, -y Y      Bildschirm-Koordinaten (absolut, wie im vorherigen
#                  Screenshot abgelesen).
# -d              Doppelklick statt einfachem Klick.
# -s ScrollSteps  Scrollen statt klicken - NICHT unterstuetzt, siehe die
#                  Einschraenkung weiter unten im Kommentar.
#
# Beispiele:
#   .claude/skills/app-testen/scripts/Send-Click.sh -x 640 -y 420
#   .claude/skills/app-testen/scripts/Send-Click.sh -x 640 -y 420 -d
#
# WICHTIGE EINSCHRAENKUNG (Scroll): cliclick 5.1 (per `cliclick -h`
# tatsaechlich geprueft, nicht nur vermutet) hat KEIN Kommando fuer
# Mausrad-/Scroll-Events - "w:" in dieser Version ist "WAIT", nicht "wheel".
# Es gibt in cliclick also keinen Weg zu echtem, koordinatenbasiertem
# Scrollen, der Win32s `mouse_event(MOUSEEVENTF_WHEEL, ...)` entspricht.
# "-s" bricht deshalb bewusst mit einer klaren Fehlermeldung ab, statt eine
# nur ungefaehre Tastatur-Naeherung (z. B. Pfeiltasten/Page-Down ueber
# cliclicks "kp:"-Kommando) stillschweigend als gleichwertigen Ersatz zu
# verwenden - eine solche Naeherung ist hier bewusst NICHT implementiert.
# Falls ein Testfall zwingend simuliertes Scrollen braucht: den User bitten,
# selbst zu scrollen, oder gezielt nachfragen, bevor eine Naeherungsloesung
# gebaut wird.
#
# Grundprinzip 4 aus ../SKILL.md gilt hier genauso: den echten Mauszeiger nie
# ohne vorherige Fokus-Pruefung/Rueckfrage bewegen, falls der User gerade in
# einer anderen Anwendung arbeiten koennte.

set -euo pipefail

X=""
Y=""
SCROLL=0
DOUBLE=0

while getopts "x:y:s:d" opt; do
    case "$opt" in
        x) X="$OPTARG" ;;
        y) Y="$OPTARG" ;;
        s) SCROLL="$OPTARG" ;;
        d) DOUBLE=1 ;;
        *)
            echo "Usage: $0 -x X -y Y [-d] [-s ScrollSteps]" >&2
            exit 1
            ;;
    esac
done

if [[ -z "$X" || -z "$Y" ]]; then
    echo "Usage: $0 -x X -y Y [-d] [-s ScrollSteps]" >&2
    exit 1
fi

if ! command -v cliclick >/dev/null 2>&1; then
    echo "cliclick ist nicht installiert - das ist Voraussetzung fuer dieses" >&2
    echo "Skript (brew install cliclick). Siehe ../SKILL.md, Abschnitt" >&2
    echo "\"macOS-Variante\"." >&2
    exit 1
fi

if [[ "$SCROLL" -ne 0 ]]; then
    echo "Echtes, koordinatenbasiertes Mausrad-Scrollen ist mit cliclick" >&2
    echo "5.1 nicht moeglich - diese Version kennt kein Wheel-/Scroll-" >&2
    echo "Kommando (siehe Kommentar oben im Skript). '-s' wird deshalb" >&2
    echo "bewusst nicht ausgefuehrt." >&2
    exit 2
fi

if [[ "$DOUBLE" -eq 1 ]]; then
    cliclick "dc:${X},${Y}"
    echo "Doppelt geklickt bei ($X,$Y)."
else
    cliclick "c:${X},${Y}"
    echo "Geklickt bei ($X,$Y)."
fi
