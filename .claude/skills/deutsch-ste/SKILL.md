---
name: deutsch-ste
description: Schreibregeln "Deutsch-STE" - eine deutsche Variante von ASD-STE100 (Simplified Technical English). Nutzen, bevor du in diesem Repo einen Feature-Plan, Umsetzungsplan.md, Test-Umsetzungsplan.md, einen Skill oder ein ähnliches Projektdokument neu schreibst oder einen Abschnitt darin änderst. Auch nutzen, wenn du so ein Dokument für eine Aufgabe liest und es noch nicht in Deutsch-STE ist.
---

# Deutsch-STE: Schreibregeln für Projektdokumente

Deutsch-STE überträgt die Regeln von ASD-STE100 auf Deutsch. Für Deutsch gibt
es keine offizielle Spezifikation mit Wörterbuch. Darum gelten hier die
Regeln unten und das Glossar am Ende. Das Ziel ist: Ein Mensch oder eine KI
liest einen Satz einmal und versteht ihn eindeutig.

## Geltungsbereich

Deutsch-STE gilt für diese Dokumente:

- Feature-Pläne im Feature-Plan-Verzeichnis und im Feature-Plan-Archiv,
  auch für den Status-Abschnitt "✅ Umgesetzt".
- `Umsetzungsplan.md` und `Test-Umsetzungsplan.md`.
- Skills unter `.claude/skills/`.
- Neue Dokumente derselben Art, zum Beispiel ein Konzept für Entwickler.

Deutsch-STE gilt nicht für diese Texte:

- `README.md`. Die README ist für Benutzer und bleibt englisch.
- `CLAUDE.md`. Diese Datei ist englisch und folgt ASD-STE100
  (Simplified Technical English), nicht Deutsch-STE.
- UI-Texte. Sie bleiben englisch (siehe `CLAUDE.md`).
- Code, Code-Kommentare und Bezeichner. Sie bleiben englisch.

Ein vorhandenes Dokument in einer anderen Sprache oder einem anderen Stil
mischst du nicht. Ergänze es im vorhandenen Stil. Stelle es nur als Ganzes
auf Deutsch-STE um, und nur wenn der Entwickler das will.

**Hinweis bei nicht umgestellten Dokumenten:** Manchmal liest oder änderst
du für eine Aufgabe ein Dokument aus dem Geltungsbereich, das noch nicht in
Deutsch-STE ist. Dann sage das dem Entwickler sofort. Frage ihn direkt, ob
er das Dokument in Deutsch-STE haben will. Diese Regeln gelten für den
Hinweis:

- Nenne das Dokument mit seinem Dateinamen.
- Stelle die Frage einmal pro Dokument und Sitzung.
- Wenn die Aufgabe das Dokument ändert, stelle die Frage vor der Änderung.
  Die Antwort bestimmt den Stil der Änderung.
- Wenn die Aufgabe das Dokument nur liest, stelle die Frage und arbeite
  weiter.
- Wenn der Entwickler "nein" sagt, ergänze das Dokument im vorhandenen
  Stil.

## Regeln für Wörter

1. Verwende für einen Begriff immer dasselbe Wort. Wechsle nicht zu einem
   Synonym. Die festen Begriffe stehen im Glossar.
2. Verwende einfache, bekannte Wörter. Lass Füllwörter weg, zum Beispiel
   "eigentlich", "halt", "quasi", "grundsätzlich", "natürlich".
3. Verwende Verben statt Substantiven auf "-ung". Schreibe "die App prüft
   den Namen", nicht "die Prüfung des Namens erfolgt durch die App".
4. Schreibe Abkürzungen aus: "zum Beispiel" statt "z. B.", "wenn nötig"
   statt "ggf.", "das heißt" statt "d. h.". Ersetze "bzw." durch "oder" oder
   durch "und".
5. Modalverben haben genau eine Bedeutung:
   - "muss" ist eine Pflicht.
   - "darf nicht" ist ein Verbot.
   - "kann" ist eine Möglichkeit.
   Verwende nicht "sollte", "könnte" oder "würde".
6. Lass technische Namen unverändert. Setze Code-Bezeichner, Dateinamen,
   Befehle und Pfade in Backticks. Setze UI-Texte in Anführungszeichen,
   genau wie die App sie zeigt.
7. Feste Fachbegriffe des Projekts bleiben englisch, wenn das Glossar es so
   sagt (zum Beispiel "Slot", "Leader", "Catch-up"). Für andere Wörter nimm
   das deutsche Wort, wenn es klar ist.

## Regeln für Sätze

1. Ein Satz hat einen Gedanken. Eine Anweisung hat eine Handlung.
2. Eine Anweisung hat höchstens 20 Wörter. Ein beschreibender Satz hat
   höchstens 25 Wörter. Code-Bezeichner zählen als ein Wort.
3. Schreibe Anweisungen im Imperativ, Du-Form, mit dem Verb am Anfang:
   "Prüfe den Namen." Nimm, wenn möglich, ein Verb ohne abtrennbaren Teil:
   "Ergänze den Namen", nicht "Nimm den Namen in die Liste auf".
4. Schreibe im Aktiv und nenne den Handelnden: "Die App speichert den
   Namen", nicht "Der Name wird gespeichert".
5. Verwende das Präsens. Verwende die Vergangenheit nur für Vorfälle, die
   schon passiert sind. Verwende keinen Konjunktiv.
6. Ein Satz hat höchstens einen Nebensatz. Halte Hilfsverb und Partizip nah
   zusammen. Schreibe keine langen Einschübe dazwischen.
7. Nenne die Bedingung zuerst: "Wenn der Slot ein Passwort hat, schlägt der
   Login fehl."
8. Verbinde Sätze nicht mit Gedankenstrichen. Schreibe zwei Sätze.
9. Verwende Klammern nur für Verweise, Code-Bezeichner und kurze Beispiele.
10. Verwende keine doppelte Verneinung.

## Regeln für Absätze und Struktur

1. Ein Absatz hat ein Thema und höchstens 6 Sätze.
2. Schreibe Schritte als nummerierte Liste. Ein Schritt hat eine Handlung.
3. Schreibe gleichrangige Punkte als Aufzählung, nicht als langen Satz.
4. Stelle Pflichtregeln und Warnungen an den Anfang ihres Abschnitts. Setze
   sie fett.
5. Eine Überschrift ist kurz und sagt, was im Abschnitt steht.
6. Schreibe Begründungen als eigenen Satz nach der Regel: erst "Was", dann
   "Warum".

## Beispiele

Vorher:
> Hints kommen beim Verbinden ohnehin vollständig vom Server, sodass hier
> ggf. nichts nachgeholt werden müsste.

Nachher:
> Der Server sendet beim Verbinden alle Hints. Darum muss die App keine
> Hints nachholen.

Vorher:
> Bausteine werden bewusst nicht als vierte `RightPanelView` umgesetzt -
> sonst würde z. B. die Hint-Ansicht ihn verdecken.

Nachher:
> Mache das Panel nicht zu einer vierten `RightPanelView`. Sonst verdeckt
> zum Beispiel die Hint-Ansicht das Panel.

## Prüfung vor dem Speichern

Prüfe jeden neuen oder geänderten Absatz:

1. Hat jeder Satz höchstens 20 Wörter (Anweisung) oder 25 Wörter
   (Beschreibung)?
2. Steht ein Satz im Passiv oder im Konjunktiv? Wenn ja, schreibe ihn um.
3. Hat ein Begriff mehr als ein Wort? Wenn ja, nimm das Wort aus dem
   Glossar.
4. Finde verbotene Wörter und Abkürzungen mit diesem Befehl:

```bash
grep -nE '\b(sollte|sollten|könnte|könnten|würde|würden|eigentlich|quasi|halt)\b|z\. ?B\.|ggf\.|bzw\.|d\. ?h\.|u\. ?a\.' <datei>.md
```

## Glossar

Ergänze dieses Glossar, wenn ein neuer fester Begriff nötig ist. Verwende
dann nur das Wort aus dem Glossar.

| Begriff | Bedeutung | Nicht verwenden |
|---|---|---|
| Gruppe | Ein Archipelago-Raum mit Host, Port und Passwort. In der App ein Tab (`ServerConnectionGroup`). | Welt, Server-Profil |
| Raum | Das Multiworld-Spiel auf dem Archipelago-Server. | Session (für den Raum) |
| Slot | Ein Spieler im Raum. | Spieler-Account |
| eigener Slot | Ein Slot, den der Benutzer konfiguriert hat (`SlotProfile`). | konfiguriertes Profil |
| fremder Slot | Ein Slot im Raum, der kein eigener Slot ist. | anderer Account |
| Sibling-Slot | Ein anderer eigener Slot derselben Gruppe, aus Sicht eines bestimmten Slots (meist des Leaders). | Geschwister-Slot, Nachbar-Slot |
| Leader | Der eine eigene Slot einer Gruppe mit einer offenen Verbindung. | Haupt-Slot |
| Catch-up | Kurzer Login, der verpasste Daten holt und dann trennt. | Sync, Nachladen |
| Roster | Die Liste aller Slots im Raum. | Spielerliste |
| Hint | Ein Hinweis von Archipelago auf einen Item-Ort. | Tipp |
| Item | Ein Gegenstand, den ein Slot empfängt oder sendet. | Gegenstand |
| Progression-Item | Ein Item mit `ItemKind == ItemProgression`. | wichtiges Item |
| Event-Log | Die Liste der Ereignisse im linken Panel. | Log, Verlauf |
| Harness | Das Projekt `TestHarness/` für UI-Prototypen. | Testprojekt |
| Entwickler | Die Person, die dieses Repo betreut und entscheidet. | User (für diese Person) |
| Benutzer | Eine Person, die die App verwendet. | User |
