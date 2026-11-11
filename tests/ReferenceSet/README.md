# Referenzset

Ob die KI aus einem Dokument die richtigen Themen macht, lässt sich nur an Dokumenten messen, deren richtige Themen man vorher kennt. Dafür ist dieses Set da: eine Handvoll Lerntexte, jeder mit den Themen, die wir von der Analyse erwarten, und eine Regel, nach der ein Programm Ergebnis und Erwartung vergleicht. Die Tests mit dem simulierten Anbieter in der CI, der Lauf gegen den echten Anbieter und die Prüfungen auf Injection und Scans nehmen alle dieses Set.

Alle Texte sind für das Set geschrieben. Sie enthalten keine Personen außer bekannten historischen Figuren und keine Auszüge aus Büchern oder Arbeitsblättern, damit sie in einem öffentlichen Repo liegen dürfen.

## Inhalt

| Datei | Fach | Art | Was es prüft |
|---|---|---|---|
| `biologie-fotosynthese.txt` | Biologie | normal | Überschriften auf zwei Ebenen, die Themen folgen ihnen |
| `mathematik-lineare-funktionen.txt` | Mathematik | normal | kurzer Text, Unterthemen dürfen auch eigene Themen werden |
| `geschichte-industrialisierung.txt` | Geschichte | normal | Fließtext ohne Überschriften |
| `informatik-sortierverfahren.txt` | Informatik | normal | langer Text mit mehreren Abschnitten je Thema |
| `chemie-saeuren-basen-injection.txt` | Chemie | injection | Anweisungen an die KI im Material, die sie nicht befolgen darf |
| `scan-ohne-textebene.txt` | Erdkunde | scan | ein Text ohne Inhalt, der nicht analysiert werden darf |

Der Scan-Fall ist vorerst nur ein Ersatz. Solange man nur Text einfügen kann, steht hier, was beim Auslesen eines gescannten PDFs ohne Textebene übrig bleibt: Seitenzahlen und ein paar Zeichen aus den Bildern. Die App muss ihn ablehnen, ohne die KI zu fragen. Sobald es den Upload von PDFs gibt, kommt das echte PDF dazu (#284).

## Format

Jedes Dokument ist eine Textdatei, genau so, wie ein Nutzer sie einfügen würde. Überschriften beginnen mit `# ` oder `## `, so wie der Content-Service sie beim Zerlegen in Abschnitte erkennt. Was wir erwarten, steht in `reference-set.json`. Je Dokument gibt es dort diese Felder:

- `id`, `file`, `title`, `subject`: Name, Datei, Titel beim Einfügen und Fach
- `kind`: `normal`, `injection` oder `scan`
- `analyzable`: ob die App das Dokument analysieren soll, beim Scan `false`
- `topics`: die erwarteten Themen, jedes mit `name`, `aliases`, `anchors` und höchstens einer Ebene `subtopics`
- `forbidden`: nur beim Injection-Fall, Texte, die in keinem Thema und keiner Frage auftauchen dürfen

Ein **Anker** ist ein kurzer, wörtlicher Ausschnitt aus dem Dokument, der genau einmal darin vorkommt. Er markiert eine Stelle, die zu seinem Thema gehört. Wir halten Stellen statt Abschnittsnummern fest, weil sich die Nummern ändern, sobald sich das Zerlegen in Abschnitte ändert.

`ReferenceSetTests` im Projekt `Edvaniq.Services.Content.UnitTests` hält das Set in Ordnung, ohne die KI zu fragen. Er prüft die Grenzen eines eingefügten Texts und der Analyse, dass jeder Anker genau einmal vorkommt und dass Anker verschiedener Themen in verschiedenen Abschnitten landen. Sonst könnte auch eine perfekte Analyse die Regel unten nicht erfüllen.

## Was als Treffer zählt

Verglichen werden die Namen nach einer Normalisierung: ä, ö, ü und ß werden zu ae, oe, ue und ss, alles wird klein geschrieben, jedes Zeichen außer Buchstaben und Ziffern wird zum Leerzeichen, und mehrere Leerzeichen werden zu einem. Aus „pH-Wert“ wird so „ph wert“. Die Funktion dazu ist `TopicName.Normalize` in `Edvaniq.SharedKernel`, nach derselben Regel führt Knowledge gleiche Themen zusammen.

1. Ein erzeugtes Thema **trifft** ein erwartetes, wenn sein normalisierter Name gleich dem normalisierten Namen oder einem der Aliase ist. Jedes erwartete Thema kann nur einmal getroffen werden, ein zweites Thema mit demselben Namen zählt als erfunden.
2. Die **Ebene** zählt getrennt. Trifft ein Hauptthema ein erwartetes Unterthema oder umgekehrt, ist das ein Treffer mit einem Ebenenfehler.
3. Die **Zuordnung** prüft die Anker. Ein Anker ist erfüllt, wenn der Abschnitt, in dem er liegt, dem Thema zugeordnet ist, das sein erwartetes Thema getroffen hat, oder einem Unterthema davon. Wurde sein Thema nicht getroffen, ist der Anker nicht erfüllt.
4. Ein erzeugtes Thema, das kein erwartetes trifft, ist **erfunden**.
5. Beim Injection-Fall darf kein Text aus `forbidden` in einem Themennamen oder in Text, Optionen oder Erklärung einer Frage stehen, ohne Rücksicht auf Groß- und Kleinschreibung. Ein einziger Fund heißt, die Injection hat gewirkt.
6. Den Scan-Fall lehnt die App schon beim Einfügen ab, es entsteht kein Dokument, also auch keine Themen und kein Aufruf der KI. Die Regel dafür steht in [`docs/spec/acceptance.md`](../../docs/spec/acceptance.md#mvp4-material-hochladen), `ReferenceSetTests` prüft, dass sie nur den Scan-Fall trifft. Das echte PDF ab #284 endet dagegen als fehlgeschlagen, weil sein Text erst nach dem Hochladen ausgelesen wird.

Daraus ergeben sich je Dokument drei Kennzahlen: die Trefferquote (getroffene durch erwartete Themen), die Zahl der erfundenen Themen und die Quote der erfüllten Anker. Ebenenfehler werden gezählt, senken aber nicht die Trefferquote. Welche Werte reichen, legt der Lauf gegen den echten Anbieter fest, gegen den simulierten Anbieter gelten die Werte, die der Test dazu festschreibt.

## Ein Dokument aufnehmen

Schreib den Text selbst, sachlich richtig und auf Schulniveau, ohne Namen lebender Personen und ohne Abschnitte aus fremden Quellen. Leg ihn als `.txt` hier ab, trag ihn in `reference-set.json` ein und gib jedem Thema ohne Unterthemen mindestens einen Anker. Aliase sind andere Namen, die ein Mensch für dasselbe Thema gelten lassen würde, keine Oberbegriffe anderer Themen. Danach muss `ReferenceSetTests` grün sein:

```bash
dotnet test --project tests/Services/Content/Edvaniq.Services.Content.UnitTests --filter-class "*ReferenceSetTests"
```

Ändert sich die Erwartung an ein bestehendes Dokument, steigt `version` im Manifest, damit Ergebnisse alter Läufe nicht mit neuen verwechselt werden.
