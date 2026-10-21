# Modusregeln

Der Modus legt fest, wie die Antworten einer Aktivität zählen und was dabei erlaubt ist ([Glossar](glossary.md#modus)). Diese Seite nennt für Übung, Einstufung und Prüfungsmodus, was der Nutzer nach einer Antwort sieht, welche Hilfen es gibt und wie sich Antworten auf Mastery und XP auswirken. Jede Antwort zählt in genau einem Modus und trägt ihn als Quelle im Lern-Nachweis.

In allen drei Modi wird jede Antwort sofort gespeichert, und doppeltes Absenden zählt nur einmal. Die Antwortzeit wird mitgespeichert, fließt aber nicht in die Mastery ein.

Regeln ohne Kennzeichnung gelten schon im MVP. Das MVP-Plus (Meilenstein M7) besteht aus optionalen Paketen, die erst nach dem MVP-Kern dazukommen. Für die Modi zählen vier davon:

- P1: XP, Level und Streak
- P2: adaptive Schwierigkeit und „Erklär es anders“
- P4: Prüfungsmodus
- P5: Tutor

Steht hinter einer Regel „(Vorschlag)“, ist sie noch nicht beschlossen.

## Übung

Die Übung ist der Normalfall und umfasst Quiz, Checkfragen der Lerneinheit „Neues Thema“, die Fragen der Wiederholung und Karten. Der Nutzer bekommt nach jeder Antwort eine Rückmeldung und darf sich die Lösung anzeigen lassen.

| Regel | Was gilt |
|---|---|
| Feedback | Nach jeder Antwort erscheinen sofort richtig oder falsch, die Lösung und die gespeicherte Erklärung. Die Lösung kommt erst nach der Antwort, die Erklärung aus dem Fragenpool ohne neuen KI-Aufruf. Bei einer Karte deckt der Nutzer die Lösung selbst auf und bewertet sich danach. Die Rückmeldung ist ermutigend formuliert. |
| Hinweise | Die Wahl von „Lösung anzeigen“ zählt als falsch. Weitere Hinweise vor der Antwort gibt es im MVP nicht (Vorschlag). |
| XP | Im MVP gibt es keine XP. Mit Plus P1 bringt eine richtige Antwort XP nach Schwierigkeit 1 bis 3, eine falsche keine. |
| Mastery-Wirkung | Antworten zählen, sobald das Thema eingeführt ist, und die Checkfragen der Lerneinheit zählen mit, wenn die Einheit abgeschlossen ist. Neuere und schwerere Antworten wirken stärker, eine Kartenbewertung wirkt schwächer als eine Quizantwort. Ohne Wiederholung sinkt der Wert mit der Zeit, und Antworten auf gemeldete Items zählen nicht. Der Startwert eines Themas kommt aus der Einstufung, ohne sie gilt ein Standardwert. Die Zahlenwerte stehen in der versionierten Regel der Mastery-Berechnung. |
| Tutor | Im MVP gibt es keinen Tutor. Mit Plus P5 steht er zur Verfügung, und am Quiz-Feedback erscheinen die Buttons „Erklär es anders“. |
| Zeitlimit | Keines. |
| Fragenauswahl | Im Quiz erscheinen kürzlich beantwortete Fragen nicht erneut, solange es Alternativen gibt, Themen mit Schwäche kommen bevorzugt dran, und die Schwierigkeit ist gemischt. Ist der Fragenpool zu klein, erscheint ein Hinweis. Die Lerneinheit „Neues Thema“ stellt zwei bis drei Checkfragen, bevorzugt leichte. Mit Plus P2 richtet sich die Schwierigkeit nach der Mastery zu Beginn des Quiz und nach Serien richtiger oder falscher Antworten. |
| Überspringen und Abbrechen | Eine einzelne Frage lässt sich nicht überspringen, wer sie nicht beantworten will, wählt „Lösung anzeigen“ (Vorschlag). Ein Abbruch ist jederzeit möglich, das Beantwortete zählt als Teilwertung, und beim Neustart geht es mit dem Rest weiter. |
| Quelle im Lern-Nachweis | Übung, im Code `Practice` (Vorschlag). |

## Einstufung

Die Einstufung stellt zu Beginn eines Lernziels das Vorwissen fest. Der Nutzer bewertet jedes Thema selbst von 1 bis 5 und kann danach einen Kurztest machen, beides lässt sich überspringen. Die Tabelle beschreibt den Kurztest, die Selbsteinschätzung kommt nur in der Zeile zur Mastery vor.

| Regel | Was gilt |
|---|---|
| Feedback | Bis zum Ende gibt es kein Feedback und keine Lösung. Das Ergebnis liegt erst am Ende vor. |
| Hinweise | Keine, der Kurztest läuft ohne Hilfe. |
| XP | Keine, auch nicht mit Plus P1. Auch die Selbsteinschätzung bringt keine XP. |
| Mastery-Wirkung | Die Einstufung wirkt nur auf den Startwert der Themen. Die Selbsteinschätzung legt ihn fest, der Kurztest korrigiert ihn für die getesteten Themen, und nicht getestete Themen behalten ihren Wert. Ohne Kurztest bleibt die Selbsteinschätzung maßgeblich, ohne Selbsteinschätzung gilt ein Standardwert. Kurztest-Antworten zählen nicht in Trefferquote und Schwächenerkennung (Vorschlag). |
| Tutor | Im MVP gibt es keinen Tutor. Mit Plus P5 bleibt er während des Kurztests gesperrt (Vorschlag). |
| Zeitlimit | Keines. |
| Fragenauswahl | Je Thema der oberen Ebene kommen ein bis zwei feste Fragen aus dem Fragenpool, nicht adaptiv und auch mit Plus P2 nicht. Bei Wiederaufnahme erscheinen dieselben Fragen, und gemeldete oder pausierte Fragen kommen nicht vor. Solange der Fragenpool eines Themas der oberen Ebene nicht bereit ist, gibt es den Kurztest nicht. |
| Überspringen und Abbrechen | Der Kurztest lässt sich überspringen und abbrechen, ohne negative Folgen. Pro Lernziel lässt er sich nur einmal auslösen. |
| Quelle im Lern-Nachweis | Einstufung, im Code `Placement`. |

## Prüfungsmodus (Plus P4)

Der Prüfungsmodus ist eine Quiz-Variante, mit der der Nutzer eine echte Prüfung übt. Jeder Durchlauf hat ein Zeitlimit und eine feste Fragenzahl über alle Themen. Im MVP gibt es den Prüfungsmodus nicht, alle Regeln dieser Tabelle gelten erst mit dem Paket P4.

| Regel | Was gilt |
|---|---|
| Feedback | Während des Durchlaufs gibt es kein Feedback. Lösungen und Erklärungen sind erst nach der Abgabe abrufbar. Das Ergebnis erscheint in Prozent und je Thema, mit Verweis auf Schwächen und dem Hinweis „Orientierung, keine Note“. Es gibt weder eine Note noch einen Notenschlüssel. |
| Hinweise | Keine Hinweise und keine Lösung während des Durchlaufs. |
| XP | Mit Plus P1 bringen richtige Antworten XP nach Schwierigkeit wie in der Übung (Vorschlag). |
| Mastery-Wirkung | Die Antworten zählen für die Mastery der Themen. Sie wirken so stark wie Quizantworten der Übung (Vorschlag). |
| Tutor | Gesperrt, sobald es den Tutor gibt (Plus P5). Die Sperre gilt auf dem Server, solange ein Durchlauf aktiv ist, und der Tutor lehnt Anfragen mit einer erklärenden Meldung ab. Auch die Buttons „Erklär es anders“ fehlen. Nach dem Durchlauf ist der Tutor wieder erreichbar. |
| Zeitlimit | Fest je Durchlauf, der Server setzt es durch. Antworten nach Ablauf werden abgewiesen, und der Durchlauf endet mit den bis dahin gegebenen Antworten. Wie lang die Zeit ist, legt das Paket P4 fest. |
| Fragenauswahl | Eine feste Fragenzahl, gleichmäßig auf alle Themen verteilt. Die Schwierigkeit passt sich nicht an (Vorschlag). Ausgeblendete und gemeldete Fragen kommen nicht vor. Reicht der Fragenpool nicht, erscheint ein Hinweis mit reduzierter Fragenzahl, oder der Durchlauf wird abgelehnt. |
| Überspringen und Abbrechen | Nicht beantwortete Fragen zählen als falsch. Pausieren lässt sich ein Durchlauf nicht, nach einem Verbindungsabbruch geht es innerhalb des Zeitlimits weiter. |
| Neben einer Session | Pro Nutzer läuft nur ein Durchlauf, und er läuft nie gleichzeitig mit einer Session. Wer bei aktiver Session einen Durchlauf startet, kann die Session beenden oder fortsetzen. Eine Session lässt sich während eines Durchlaufs nicht starten, und die Zeit des Durchlaufs zählt nicht als aktive Lernzeit. |
| Quelle im Lern-Nachweis | Prüfungsmodus, im Code `MockExam`. |
