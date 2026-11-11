# Texte für Rückmeldungen

Viele, die mit Edvaniq lernen, sind Schüler, und wer nach einer Frage ein Urteil über sich liest, übt beim nächsten Mal weniger gern. Deshalb ist jede Rückmeldung der App ermutigend: Sie sagt, was stimmt, zeigt die Lösung und den nächsten Schritt und bewertet nie die Person. Das gilt für die Rückmeldung nach einer Antwort genauso wie für den Abschluss einer Session und für Hinweise, wenn etwas nicht klappt.

## Wo die Texte stehen

Alle Rückmeldungen stehen in `FeedbackTexts` in `Client.UI` (`src/Clients/Edvaniq.Client.UI/Feedback/FeedbackTexts.cs`). Eine Seite schreibt keinen eigenen Satz für eine Rückmeldung, sie holt ihn von dort. So lassen sich alle Texte auf einmal gegenlesen, und der Test unten sieht jeden.

Für das Ende einer Session gibt es je Ergebniszustand einen Titel und einen Text, abrufbar mit `FeedbackTexts.For(SessionResult)`. Die Zustände sind sehr gut, gut, gemischt, viele Fehler, Abbruch und keine Fragen. Welche Zahl richtiger Antworten zu welchem Zustand führt, legen die Ergebnisdaten der Session fest, nicht die Texte. Ein Abbruch folgt den [Modusregeln](spec/modes.md#übung): Das Beantwortete zählt, und beim nächsten Start geht es mit dem Rest weiter.

Text von der KI, also Fragen, Antwortoptionen und Erklärungen, läuft nicht über `FeedbackTexts` und wird von diesen Regeln nicht geprüft.

## Ton

Die App duzt, in kurzen Sätzen. Nach einer falschen Antwort steht „Diesmal nicht richtig.“ und daneben die Lösung mit Erklärung, nicht „Falsch“. Wer sich die Lösung anzeigen lässt, hat nicht geantwortet und bekommt deshalb kein Urteil, sondern „Hier ist die Lösung.“, auch wenn das für die Mastery als falsch zählt. Ein Hinweis bei einem Fehler der App sagt, was passiert ist und was der Nutzer tun kann, und gibt ihm nie die Schuld.

Höchstens ein Ausrufezeichen je Text, und nur für Lob. Ein Text vergleicht nicht mit anderen und setzt keine Frist.

## Was ein Text nicht enthält

`FeedbackTextTests` prüft jeden Text aus `FeedbackTexts` gegen diese Regeln und zeigt mit einem Gegentest je Regel, dass sie anschlägt. Der Test sucht die Namen der Regeln in dieser Datei, benenne sie also nur zusammen mit dem Test um.

| Regel | Beispiel | Warum |
|---|---|---|
| Urteil „falsch“ | „Falsch.“ | Bewertet die Antwort wie eine Note. „Diesmal nicht richtig“ sagt dasselbe und lässt den nächsten Versuch offen. |
| Bedauern „leider“ | „Leider nicht richtig.“ | Klingt enttäuscht, als hätte der Nutzer jemanden im Stich gelassen. |
| Vorwurf der Wiederholung | „Schon wieder daneben.“, „immer noch“ | Zählt mit und hält Fehler vor. |
| Druck durch Pflicht | „Du musst mehr üben.“, „solltest“ | Macht aus Lernen eine Pflicht, die man verfehlen kann. |
| Urteil über die Person | „schlecht“, „schwach“, „versagt“, „durchgefallen“, „enttäuschend“ | Bewertet den Menschen statt den Stand eines Themas. |
| Zeitdruck | „Beeil dich.“, „endlich“, „sofort“ | Edvaniq hat in der Übung kein Zeitlimit, ein Text erfindet keines. |
| Kleinreden | „Nur 3 von 10 richtig.“ | Macht ein Ergebnis klein, statt es zu nennen. |
| mehr als ein Ausrufezeichen | „Super!!“ | Wirkt laut, Lob braucht keine Kette. |
| Großbuchstaben | „NICHT richtig“ | Liest sich wie Anschreien. |

Ein neuer Text kommt nach `FeedbackTexts` und wird vor dem Merge von zwei Teammitgliedern gegengelesen. Der Test fängt die groben Fälle, ob ein Satz freundlich klingt, entscheidet ein Mensch.