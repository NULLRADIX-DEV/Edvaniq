# Barrierefreiheit

Viele, die mit Edvaniq lernen, nutzen keine Maus, sehen schlecht oder lassen sich vorlesen. Deshalb ist eine Änderung an der Oberfläche erst fertig, wenn sie die Checkliste unten erfüllt. Ziel ist WCAG 2.2 Stufe AA, verbindlich sind die vier Punkte der Basis-Barrierefreiheit aus [Q19 und Q20](spec/quality.md#barrierefreiheit). Wie der automatische Check aufgebaut ist und wie du ihn lokal startest, steht in der [Architektur](architecture.md#barrierefreiheits-check).

## Definition of Done

Sie gilt für jeden Pull Request, der ändert, was ein Client zeigt, also Seiten, Komponenten und Stile unter `src/Clients`. Fertig ist die Änderung, wenn alles davon stimmt:

1. Die CI ist grün, auch `Edvaniq.Web.AccessibilityTests` und `ContrastTests`.
2. Jede neue Seite und jeder neue Zustand einer Seite wird von axe-core gescannt, bei 375 px und bei 1280 px Breite. Eine Seite kommt dafür in die Listen von `ShellScanTests`, ein Zustand, den man erst herbeiführen muss, bekommt einen eigenen Test wie in `TopicsPageTests` und `QuizPageTests`.
3. Jede neue Farbe hat in `ContrastTests` ihr Paar mit dem Hintergrund, auf dem sie steht.
4. Die manuellen Prüfungen der Checkliste sind für die geänderte Ansicht gemacht.

## Review

Wer reviewt, geht die Checkliste für die geänderte Ansicht durch und macht die manuellen Prüfungen selbst. Was du geprüft hast, schreibst du in den Kommentar deines Approvals, etwa „Tastatur und Accessibility-Baum von `/themen` geprüft, keine neue Farbe“. Die Beschreibung des Pull Requests bleibt kurz, die Checkliste steht nur hier.

## Checkliste

Zu jedem Punkt steht, woran du ihn erkennst, was automatisch geprüft wird und was ein Mensch prüfen muss. Was ein Test nicht sieht, ist nicht erledigt, nur weil die CI grün ist. `AccessibilityChecklistTests` liest die vier Überschriften dieses Abschnitts und die hier genannten Testklassen. Benenne sie nicht um, ohne den Test anzupassen.

### Tastatur

Jedes Bedienelement ist mit Tab erreichbar, in der Reihenfolge, in der man die Seite liest, und lässt sich mit Enter oder Leertaste auslösen. Was sich öffnet, schließt Escape, und der Fokus kehrt dorthin zurück, wo er herkam. Das Element mit dem Fokus zeigt immer einen sichtbaren Rahmen, die Stile setzen dafür `--focus-ring`.

Automatisch prüft `KeyboardTests` die Shell: Der Skip-Link ist der erste Stopp und setzt den Fokus auf den Inhalt, das Menü öffnet und schließt sich mit Enter, Leertaste und Escape, und jeder Stopp der Startseite zeigt einen Fokusrahmen von mindestens 2 px. Ein neues Bedienelement prüfst du manuell: Geh die geänderte Ansicht einmal ganz ohne Maus durch, vorwärts mit Tab und zurück mit Umschalt und Tab.

### Kontrast

Der Mindestwert folgt WCAG 2.2 AA: 4,5:1 für Text gegen seinen Hintergrund, 3:1 für Rahmen, Symbole und Bedienelemente. Eine neue Farbe kommt als Token nach `edvaniq.css`, nur dort sieht sie der Test.

Automatisch rechnet `ContrastTests` jedes eingetragene Paar aus den Tokens nach, mit genau diesen Mindestwerten. Zusätzlich misst axe-core in `ShellScanTests`, `TopicsPageTests` und `QuizPageTests` den Kontrast jedes Textes auf der gescannten Seite. Manuell prüfst du, was kein Scan sieht: Text auf einem Bild oder Verlauf und die Zustände Hover und Fokus, wenn sie die Farbe wechseln.

### Screenreader

Jedes Eingabefeld hat eine Beschriftung, jede Schaltfläche und jeder Link einen Namen, der sagt, was passiert. Bilder mit Inhalt haben einen Alternativtext, reine Zierde ist für Screenreader ausgeblendet. Eine Meldung, die ohne Seitenwechsel erscheint, etwa ein Fehler oder ein neuer Status, steht in einer Live-Region, damit sie vorgelesen wird. Jede Seite hat einen Titel und eine Überschrift erster Ebene.

Automatisch meldet axe-core in denselben Scans fehlende Beschriftungen, Namen, Alternativtexte, Seitentitel und die Sprache des Dokuments. Ob ein Name verständlich ist und ob eine Meldung wirklich vorgelesen wird, prüfst du manuell im Accessibility-Baum der Entwicklerwerkzeuge des Browsers. Vor einem Release und vor einer Abnahme kommt ein vollständiger manueller Durchgang mit einem Screenreader dazu.

### Reduzierte Bewegung

Wer im System reduzierte Bewegung eingestellt hat, sieht keine Animation und keinen Übergang. `edvaniq.css` schaltet dafür bei `prefers-reduced-motion: reduce` jede `transition` und `animation` ab, auch die einer einzelnen Komponente. Eine neue Bewegung braucht deshalb keine eigene Regel, sie darf diese aber nicht mit `!important` aushebeln.

Automatisch lädt `ReducedMotionTests` jede Seite aus `ShellScanTests` mit reduzierter Bewegung und verlangt, dass sich dort nichts bewegt, auch kein Element, das der Test mit eigener Animation hineinsetzt. Dasselbe Element muss sich ohne die Einstellung bewegen, sonst wäre der Test stumm. Manuell prüfst du nur Bewegung, die nicht aus CSS kommt, etwa ein Scrollen per Skript.
