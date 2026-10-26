# Ereigniskatalog

Services melden einander Änderungen als Ereignisse über den Broker ([Nachrichten](../architecture.md#nachrichten)). Wem ein Objekt gehört, legt das [Objektmodell](object-model.md) fest, was die Begriffe bedeuten, das [Glossar](glossary.md). Im MVP bleibt es bei diesen neun Ereignissen ([Nicht-Ziele](non-goals.md#plattform-fundament)).

Die Liste, die Felder und der Vertrag des Lern-Nachweises sind ein Entwurf. Verbindlich werden sie in Meilenstein M1, jeweils mit einem Test (#185, #190). Steht hinter einer Angabe „(Vorschlag)“, ist sie noch nicht beschlossen. Die [Regeln für Ereignisse](#regeln-für-ereignisse) gelten dagegen schon jetzt.

## Die neun Ereignisse

| Ereignis | Sender | Auslöser | Empfänger | Empfänger im MVP-Plus |
|---|---|---|---|---|
| `UserDeleted` | Identity | Ein Nutzer löscht sein Konto. | Planning, Content, Knowledge, Assessment, Flashcards, LearningEngine | Gamification, Tutor |
| `DocumentAnalyzed` | Content | Die Analyse eines Dokuments ist erfolgreich abgeschlossen. | Knowledge, Planning | – |
| `DocumentDeleted` | Content | Ein Nutzer löscht ein Dokument. | Knowledge, Assessment, Flashcards | – |
| `TopicsChanged` | Knowledge | Knowledge übernimmt Themen aus einer Analyse, oder der Nutzer ändert die Themen eines Lernziels. | Planning, Assessment, Flashcards, LearningEngine | Tutor |
| `ItemsGenerated` | Content | Der Worker von Content hat Fragen oder Karten zu einem Thema erzeugt und geprüft. | Assessment, Flashcards | – |
| `ItemFlagged` | Assessment, Flashcards | Ein Nutzer meldet eine Frage oder Karte als falsch oder unangemessen. | LearningEngine | – |
| `AnswerRecorded` | Assessment | Ein Nutzer beantwortet eine Frage. | LearningEngine | Gamification |
| `CardReviewed` | Flashcards | Ein Nutzer bewertet eine Karte. | LearningEngine | Gamification |
| `SessionCompleted` | Planning | Eine Session endet. | LearningEngine | Gamification |

Flashcards kommt mit Meilenstein M4 dazu. Bis dahin gibt es keine Karten, also auch kein `CardReviewed`. Analytics und Notifications empfangen im MVP nichts, weil sie keine Nutzerdaten speichern. Ihre Zeilen in der Event-Tabelle der [Architektur](../architecture.md#service-regeln) halten den Weg für später frei.

Ein Ereignis liegt in den `.Contracts` seines Senders, und aus diesem Namensraum ergeben sich Stream und Subject beim Broker. `ItemFlagged` gibt es deshalb zweimal, in `Edvaniq.Services.Assessment.Contracts` und in `Edvaniq.Services.Flashcards.Contracts`, mit demselben Namen und denselben Feldern. LearningEngine hört auf beide, und im Katalog zählt es als ein Ereignis. Das Beispiel `ExampleItemCreated` aus der Vorlage gehört nicht dazu, es verschwindet, sobald ein Service das Beispiel durch seine erste echte Entity ersetzt ([Einen neuen Service anlegen](../service-template.md#schritte)).

Für das Löschen eines Lernziels gibt es kein eigenes Ereignis. Auf welchem Weg Content und Knowledge davon erfahren und was nach dem Löschen eines Dokuments mit Themen und Mastery passiert, ist noch offen und wird vor dem Löschen von Lernzielen entschieden (#518).

## Felder

Jedes Ereignis trägt die `userId` des Nutzers, dem die Änderung gehört, denn jeder Empfänger speichert Daten nur je Nutzer. Die übrigen Felder stehen in der Tabelle, mit den Namen aus dem Code. Die Kennung der Nachricht und den `traceparent` legt die Outbox dazu, sie gehören nicht zum Ereignis.

| Ereignis | Felder |
|---|---|
| `UserDeleted` | `userId`. So steht es schon in `Edvaniq.Services.Identity.Contracts`. |
| `DocumentAnalyzed` | `userId`, `learningGoalId`, `documentId` und die vorgeschlagenen Themen. Ein Vorschlag nennt entweder die `topicId` eines bestehenden Themas, dem weitere Abschnitte zugeordnet werden, oder den Namen eines neuen Themas (Vorschlag). Die bestehenden Themen fragt Content vorher bei Knowledge ab. Dazu kommen das übergeordnete Thema, als `topicId` oder als Verweis auf einen anderen Vorschlag im selben Ereignis, die `sectionId` der Abschnitte und die Kernbegriffe mit Definition. Für ein neues Thema vergibt Knowledge die `topicId`. |
| `DocumentDeleted` | `userId`, `learningGoalId`, `documentId`. |
| `TopicsChanged` | `userId`, `learningGoalId`, `changedAt`, dazu `topicIds` mit allen aktiven Themen des Lernziels und `hiddenTopicIds` mit den ausgeblendeten. Ein Thema, das in keiner der beiden Listen steht, ist gelöscht. Namen und Reihenfolge trägt das Ereignis nicht, die zeigt der Client aus Knowledge an. |
| `ItemsGenerated` | `userId`, `topicId` und je Item die `itemId`, die Art (Frage oder Karte), `sectionId` und `documentId` der Quelle, Modell- und Promptversion und der Inhalt. Bei einer Frage sind das Text, Optionen, Lösung, Erklärung und Schwierigkeit, bei einer Karte Vorder- und Rückseite. Die `itemId` vergibt Content, damit eine doppelte Zustellung kein zweites Item anlegt. |
| `ItemFlagged` | `userId`, `itemId`, `topicId`, `flaggedAt`. |
| `AnswerRecorded` | Der [Lern-Nachweis](#lern-nachweis). |
| `CardReviewed` | Der [Lern-Nachweis](#lern-nachweis) und dazu `rating`, die Bewertung der Karte mit den Werten `Again`, `Hard`, `Good` und `Easy` für Nochmal, Schwer, Gut und Leicht (Vorschlag). |
| `SessionCompleted` | `userId`, `sessionId`, `endedAt`, `completed` und `introducedTopicIds`. `completed` ist wahr, wenn der Nutzer die Session abgeschlossen hat, und falsch bei einem Abbruch (Vorschlag). `introducedTopicIds` nennt die Themen, deren Lerneinheit „Neues Thema“ in dieser Session abgeschlossen wurde. |

## Lern-Nachweis

`AnswerRecorded` und `CardReviewed` tragen denselben Vertrag, den [Lern-Nachweis](glossary.md#lern-nachweis). LearningEngine legt aus jedem einen Eintrag an und berechnet aus diesen Einträgen die Mastery. Der Nachweis enthält keinen Fragetext, keine Optionen und keine Lösung, nur Schlüssel und die Werte, die LearningEngine verrechnet oder mitspeichert.

| Feld | Bedeutung |
|---|---|
| `userId` | Der Nutzer, der geantwortet hat. |
| `topicId` | Das Thema, zu dem das Item beim Antworten gehörte. |
| `itemId` | Die Frage oder Karte. |
| `sessionId` | Die Session, in der die Antwort fiel. Leer bei Antworten außerhalb einer Session, also im Kurztest der Einstufung und im Prüfungsmodus (Vorschlag). |
| `answerId` | Die Kennung der Antwort, die der Owner des Items beim Speichern vergibt. Sie ist über Fragen und Karten hinweg eindeutig, weil LearningEngine den Nachweis allein an ihr erkennt, und sie ist der Schlüssel gegen doppeltes Zählen. Kommt dieselbe Abgabe doppelt beim Owner an, etwa nach einem Doppelklick, speichert er sie nur einmal, es gibt also nur eine `answerId`. Kommt ein Ereignis doppelt bei LearningEngine an, legt es trotzdem je `answerId` nur einen Nachweis an. |
| `correct` | Richtig oder falsch. „Lösung anzeigen“ zählt als falsch. Bei einer Karte gelten Gut und Leicht als richtig, Nochmal und Schwer als falsch (Vorschlag). |
| `difficulty` | Die Schwierigkeit der Frage von 1 bis 3. Karten haben keine Schwierigkeit, bei ihnen bleibt das Feld leer (Vorschlag). |
| `responseTimeMs` | Die Zeit vom Anzeigen des Items bis zur Antwort in Millisekunden, gemessen im Client. LearningEngine speichert sie, verrechnet sie aber nicht. |
| `confidence` | Optional, wie sicher sich der Nutzer bei der Antwort war, von 1 (geraten) bis 3 (sicher) (Vorschlag). Das Feld fehlt, wenn der Client nicht danach fragt. Es wird gespeichert, aber nicht verrechnet. |
| `source` | Der Modus, in dem die Antwort zählt, also `Practice`, `Placement` oder `MockExam` ([Modusregeln](modes.md)). |
| `answeredAt` | Der Zeitpunkt der Antwort in UTC. Ihn setzt der Server des Owners beim Speichern, nicht das Gerät des Nutzers. |

Ein Lern-Nachweis bleibt 12 Monate nach `answeredAt` gespeichert und wird danach gelöscht (Vorschlag, Q27 in den [Qualitätszielen](quality.md#datenaufbewahrung-und-löschung)). Neuere Antworten zählen für die Mastery ohnehin stärker, und weil viele Nutzer minderjährig sind, hält LearningEngine nicht länger als nötig fest, wie jemand gelernt hat. Löscht ein Nutzer sein Konto, wartet LearningEngine die Frist nicht ab und löscht seine Nachweise, sobald `UserDeleted` ankommt. Wie die Mastery konsistent bleibt, wenn alte Nachweise wegfallen, ist noch offen und wird mit der Frist umgesetzt (#365).

## Regeln für Ereignisse

Ein Ereignis meldet eine Änderung, die beim Sender schon gespeichert ist, und heißt deshalb nach dem, was passiert ist, etwa `DocumentDeleted`. Es ist kein Auftrag: Der Sender wartet auf keine Antwort und weiß nicht, wer zuhört.

Ereignisse ändern sich nur additiv. Ein neues Feld ist optional, damit ein Empfänger mit dem alten Stand weiterläuft und ältere Nachrichten ohne das Feld lesbar bleiben. Ein Feld wird nie umbenannt, entfernt oder umgedeutet, und sein Typ bleibt. Eine Änderung, die nicht additiv geht, ist im MVP nicht vorgesehen.

Es gibt keine Sagas, also keinen Ablauf über mehrere Services, der Schritt für Schritt weiterreicht und bei einem Fehler zurückrollt. Jeder Empfänger handelt für sich. Eine Bestätigung zurück an den Sender gibt es auch nicht, denn sie wäre ein Ereignis in die Gegenrichtung und damit ein Kreis. Dass ein Empfänger ein Ereignis behandelt hat, zeigen seine Inbox und eine Zeile in seinem Log mit der Trace-ID ([Nachverfolgung](../architecture.md#nachverfolgung)).

Kreise löst eine Abfrage auf (Regel 6 der [Service-Regeln](../architecture.md#service-regeln)). Braucht ein Service Daten von einem Service, der auf ihn hört, fragt er sie per HTTP ab. Wer auf wen hören darf, legt die Event-Tabelle der Architektur fest, und jedes Paar aus Sender und Empfänger in diesem Katalog muss dort stehen. Das prüft `EventCatalogTests` in `Edvaniq.ArchitectureTests`.

Der Broker stellt mindestens einmal und ohne feste Reihenfolge zu. Ein Empfänger behandelt jedes Ereignis deshalb so, dass eine zweite Zustellung nichts ändert und ein älteres Ereignis kein neueres überschreibt. `TopicsChanged` trägt dafür den ganzen Stand der Themen eines Lernziels mit `changedAt` statt nur der Änderung, und ein Empfänger übernimmt nur einen Stand, der neuer ist als der zuletzt übernommene.

Ein Ereignis trägt Schlüssel und die Werte, die seine Empfänger brauchen. Inhalte trägt es nur für einen Empfänger, der sie übernimmt und ab dann besitzt, also die Themenvorschläge in `DocumentAnalyzed` für Knowledge und die Items in `ItemsGenerated`. Andere Empfänger desselben Ereignisses lesen nur die Schlüssel und speichern keinen Inhalt (Regel 5). Texte von Abschnitten verlassen Content nie, und kein Ereignis trägt den Namen, die E-Mail-Adresse oder andere Kontodaten eines Nutzers.

Alle Zeitpunkte in Ereignissen sind UTC. Den [Lerntag](glossary.md#lerntag) rechnet jeder Empfänger selbst aus, er steht in keinem Ereignis.
