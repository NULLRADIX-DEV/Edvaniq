# Glossar

Jeder Begriff steht hier einmal, mit genau einer Definition und seinem Namen im Code. Docs, Issues und Code benutzen diese Begriffe und keine anderen. Wo früher andere Wörter dasselbe meinten, nennt sie die Zeile „Nicht“, und sie gelten nicht mehr.

Die Namen im Code sind englisch und in PascalCase, passend zu `topicId`, `itemId` und `sessionId` und zu den Ereignissen `AnswerRecorded` und `CardReviewed`. Welcher Service welches Objekt besitzt, legt das [Objektmodell](object-model.md) fest. Wie sich die Modi unterscheiden, legen die [Modusregeln](modes.md) fest. Welche Ereignisse es gibt und was der Lern-Nachweis trägt, legt der [Ereigniskatalog](events.md) fest.

## Lernziel und Material

Ein Nutzer lernt auf ein Lernziel hin und bringt dafür Material mit.

### Nutzer

Eine Person, die Edvaniq mit einem Konto benutzt. Alle Lerndaten gehören genau einem Nutzer.

Im Code: `User`

### Lernziel

Das, was ein Nutzer lernen will, zum Beispiel eine Klausur oder C#. Themen und Material gehören zu genau einem Lernziel, und ein Nutzer kann mehrere aktive Lernziele haben.

Im Code: `LearningGoal`

### Art des Lernziels

Sie legt fest, ob das Lernziel auf eine Prüfung zuläuft. Bei der Prüfungsvorbereitung gibt es eine Prüfung und optional einen Prüfungstermin, und beim Freien Lernen lernt der Nutzer ein Thema ohne Prüfung.

Im Code: `LearningGoalKind` mit den Werten `ExamPreparation` und `FreeLearning`

Nicht: Modus, Zieltyp

### Prüfung

Die echte Prüfung eines Nutzers außerhalb von Edvaniq, etwa eine Klausur, Schulaufgabe oder IHK-Prüfung. Ein Lernziel hat im MVP höchstens eine Prüfung, ihre Prüfungsart ist nur ein Label.

Im Code: `Exam`

Nicht: Prüfung für den Prüfungsmodus

### Prüfungstermin

Das Datum, an dem die Prüfung stattfindet. Er ist optional, denn ohne Prüfungstermin plant der Planer rollierend für die nächsten sieben Tage.

Im Code: `ExamDate`

Nicht: Zieldatum, Prüfungsdatum

### Material

Alles, was ein Nutzer zu einem Lernziel hinzufügt, damit Edvaniq daraus Themen und Items ableitet. Es besteht aus den Dokumenten des Lernziels.

Im Code: `Material`

Nicht: Lernmaterial

### Dokument

Eine einzelne Datei (PDF, DOCX, TXT oder Markdown) oder ein eingefügter Text im Material eines Lernziels. Jedes Dokument ist hochgeladen, in Analyse, bereit oder fehlgeschlagen.

Im Code: `Document`

### Abschnitt

Ein Textstück eines Dokuments, in das die Analyse es zerlegt, mit Bezug auf Dokument und Seite. Jeder Abschnitt gehört zu einem Thema und ist die Grundlage für Zusammenfassungen, Items und Quellenangaben.

Im Code: `Section`

Nicht: Chunk

## Themen

Die KI gliedert das Material in Themen, die der Nutzer prüfen und ändern kann.

### Thema

Ein abgegrenzter Lerninhalt eines Lernziels, zu dem Zusammenfassung, Items, Mastery und Zustand gehören. Themen sind in zwei Ebenen gegliedert, in Themen und ihre Unterthemen, und behalten ihre ID, auch wenn der Nutzer sie umbenennt.

Im Code: `Topic`, die Kennung heißt `topicId`

Nicht: Skill, Bereich, Kapitel, Lernbereich

### Unterthema

Ein Thema der unteren Ebene, das zu genau einem Thema der oberen Ebene gehört. Tiefer als zwei Ebenen wird nicht gegliedert.

Im Code: `Subtopic`

### Kernbegriff

Ein Begriff mit Definition, den die Analyse im Material erkennt und einem Thema zuordnet.

Im Code: `KeyTerm`

Nicht: Fachbegriff

### Zusammenfassung

Der Text zu einem Thema, den die KI aus dessen Abschnitten schreibt, in den Längenstufen Kurz, Standard und Detailliert. Er entsteht erst beim ersten Abruf, wird dann gespeichert und nennt Dokument und Seite als Quelle.

Im Code: `Summary`

Nicht: Modus für die Längenstufen

## Fragen und Karten

Aus den Abschnitten entstehen Items, mit denen der Nutzer sein Wissen abruft.

### Item

Alles, was ein Nutzer beantworten oder bewerten kann, nämlich eine Frage oder eine Karte. Ein Item gehört zu einem Thema und nennt, wenn es aus Material entstand, seinen Abschnitt.

Im Code: `Item`, die Kennung heißt `itemId`

Nicht: Aufgabe, Lernaktivität

### Frage

Ein Item mit vorgegebenen Optionen und eindeutiger Lösung, als Single Choice, Multiple Choice oder Wahr/Falsch. Jede Frage hat eine Schwierigkeit von 1 bis 3 und eine Erklärung zur Lösung.

Im Code: `Question`

Nicht: Aufgabe, Quizfrage

### Checkfrage

Eine Frage aus dem Fragenpool, die in der Lerneinheit „Neues Thema“ nach der Zusammenfassung gestellt wird, zwei bis drei je Einheit und bevorzugt aus der leichten Stufe. Sie ist keine eigene Art Frage, sondern eine Frage in dieser Rolle.

Im Code: `CheckQuestion`

Nicht: Verständnisfrage

### Karte

Ein Item, das der Nutzer aufdeckt und selbst bewertet, mit der Frage auf der einen und der Lösung auf der anderen Seite. Die Bewertung hat vier Stufen (Nochmal, Schwer, Gut, Leicht) und bestimmt, wann die Karte wieder fällig wird.

Im Code: `Card`

Nicht: Karteikarte, Flashcard

### Fragenpool

Die Fragen eines Themas, die vorab im Hintergrund erzeugt und geprüft werden, rund 15 bis 20 je Thema. Quiz, Checkfragen und der Kurztest der Einstufung ziehen ihre Fragen daraus.

Im Code: `QuestionPool`

Nicht: Fragenkatalog

### Antwort

Was ein Nutzer auf ein Item gibt: die gewählten Optionen einer Frage oder die Bewertung einer Karte. Jede Antwort wird sofort gespeichert und hinterlässt einen Lern-Nachweis.

Im Code: `Answer`, die Kennung heißt `answerId`, gemeldet wird sie mit `AnswerRecorded` bei Fragen und `CardReviewed` bei Karten

### Lern-Nachweis

Der unveränderliche Eintrag, den eine Antwort hinterlässt, mit Nutzer, Thema, Item, Session, richtig oder falsch, Schwierigkeit, Antwortzeit, optional Confidence, Quelle (etwa Einstufung), Zeitpunkt und einem Schlüssel, der doppeltes Zählen verhindert. Aus den Lern-Nachweisen berechnet die LearningEngine die Mastery, wobei Antwortzeit und Confidence nur mitgespeichert und nicht verrechnet werden.

Im Code: `LearningRecord`

Nicht: Lern-Event, Lernnachweis

## Lernen

Ein Nutzer lernt in Sessions aus Wiederholung, Lerneinheit und Quiz, die Einstufung kommt zu Beginn eines Lernziels. Jede Antwort zählt in einem Modus.

### Session

Der Durchlauf eines Blocks, in dem der Nutzer der Reihe nach Wiederholung, Lerneinheit „Neues Thema“, Quiz und Abschluss durchgeht, soweit der Block sie vorsieht. Pro Nutzer ist höchstens eine Session aktiv.

Im Code: `Session`, die Kennung heißt `sessionId`, das Ende meldet `SessionCompleted`

Nicht: Lernsession, Tagesplan-Eintrag

### Lerneinheit „Neues Thema“

Die Einheit, in der ein Nutzer ein Thema kennenlernt: Er liest die Zusammenfassung des Themas und beantwortet danach zwei bis drei Checkfragen. Danach gilt das Thema als eingeführt.

Im Code: `IntroductionUnit`

Nicht: Erarbeitung

### Quiz

Eine Folge von Fragen aus dem Fragenpool, die ein Nutzer beantwortet. Bewertet wird binär, also richtig oder falsch, und „Lösung anzeigen“ zählt als falsch.

Im Code: `Quiz`

### Wiederholung

Der Lernschritt, der Eingeführtes auffrischt, also fällige Karten, zuletzt falsch beantwortete Fragen und Themen, deren Mastery durch Zeitverfall unter die Schwelle gefallen ist. Er steht am Anfang einer Session.

Im Code: `Review`

Nicht: Smart Review

### Einstufung

Das Feststellen des Vorwissens zu Beginn eines Lernziels, bei dem der Nutzer jedes Thema selbst von 1 (noch nie gehört) bis 5 (sicher beherrscht) bewertet und danach einen Kurztest mit ein bis zwei festen Fragen je Thema der oberen Ebene machen kann. Beides lässt sich überspringen und setzt nur den Startwert der Mastery.

Im Code: `Placement`

Nicht: Skill-Level-Test, Einstufungstest (der adaptive Test kommt erst nach dem MVP)

### Modus

Der Modus legt fest, wie die Antworten einer Aktivität zählen und was dabei erlaubt ist. Es gibt drei: Übung als Normalfall mit Rückmeldung nach jeder Antwort, Einstufung für den Kurztest und den Prüfungsmodus, eine Quiz-Variante mit Zeitlimit und ohne Hinweise, die zum optionalen MVP-Plus (Meilenstein M7) gehört.

Im Code: `Mode` mit den Werten `Practice`, `Placement` und `MockExam`

Nicht: Modus für die Art des Lernziels oder die Längenstufen einer Zusammenfassung, Prüfung für den Prüfungsmodus

## Fortschritt

Der Fortschritt eines Nutzers hängt am Thema.

### Mastery

Ein Wert von 0 bis 100 % je Nutzer und Thema, der sagt, wie gut er das Thema beherrscht. Er entsteht aus den Lern-Nachweisen, bei denen neuere und schwerere Antworten stärker zählen und ohne Wiederholung ein einfacher Zeitverfall wirkt, und lässt sich daraus innerhalb der Aufbewahrungsfrist der Lern-Nachweise neu berechnen.

Im Code: `Mastery`

Nicht: Wissensstand, Wissenswert, Skill-Level

### Themenzustand

Der Zustand eines Themas für einen Nutzer, immer genau einer von vier: neu, Schwäche, im Aufbau oder sicher. Neu ist ein Thema bis zur Einführung, danach gilt es bei Mastery unter 50 % oder hoher Fehlerquote zuletzt als Schwäche, ab 80 % als sicher und sonst als im Aufbau.

Im Code: `TopicState`

Nicht: offen (für neu), gemeistert, Stärke (für sicher)

### eingeführt

Ein Thema ist eingeführt, sobald der Nutzer die Lerneinheit „Neues Thema“ dazu abgeschlossen hat. Ab dann wirken Antworten auf die Mastery, das Thema kommt in die Wiederholung, und der Plan behandelt es nicht mehr als neu.

Im Code: `Introduced`

Nicht: erarbeitet

## Plan

Der Planer verteilt die Themen eines Nutzers auf Lerntage und füllt jeden Tag mit Blöcken.

### Lernplan

Die zeitliche Verteilung der Themen auf die Lerntage, für alle aktiven Lernziele eines Nutzers. Ein regelbasierter Planer ohne KI erstellt ihn und berechnet den Rest neu, wenn sich Mastery, Vorgaben oder Material ändern.

Im Code: `StudyPlan`

Nicht: Lernpfad (das wäre die inhaltliche Reihenfolge der Themen und kommt nach dem MVP)

### Tagesplan

Die Blöcke eines Lerntags für einen Nutzer, über alle seine aktiven Lernziele hinweg. Der Planer füllt sie, bis das Tagesbudget des Tages voll ist.

Im Code: `DayPlan`

### Block

Ein Eintrag im Tagesplan, also ein geplantes Stück Lernen wie ein neues Thema, ein Quiz oder die Wiederholung, mit Zeitbedarf in Minuten und einem Grund wie „fällig“ oder „Prüfung in 3 Tagen“. Wer einen Block startet, beginnt eine Session, und mit deren Abschluss gilt der Block als erledigt.

Im Code: `Block`

Nicht: Session, Tagesplan-Eintrag, Plan-Block

### Tagesbudget

Die Minuten, die ein Nutzer an einem Wochentag lernen will, einmal für alle seine Lernziele zusammen. Der Planer verteilt sie nach Prüfungsnähe auf die aktiven Lernziele, und solange der Nutzer kein Budget gesetzt hat, gilt ein Standardbudget.

Im Code: `DailyBudget`

Nicht: Zeitbudget, tägliche Lernzeit

### Tagesziel

Das Tagesziel ist erreicht, wenn alle für heute geplanten Blöcke erledigt sind. Ein Tag ohne Blöcke hat kein offenes Tagesziel.

Im Code: `DailyGoal`

### Lerntag

Der Tag, zu dem ein Zeitpunkt gehört, mit der Tagesgrenze um 04:00 Uhr Europe/Berlin, sodass ein Ereignis um 03:59 noch zum Vortag zählt. Alle Services speichern Zeitpunkte in UTC und bestimmen den Lerntag auf dieselbe Weise.

Im Code: `StudyDay`
