# Akzeptanzkriterien

Ein Kernpunkt gilt als fertig, wenn seine Akzeptanzkriterien erfüllt sind. Jeder Kernpunkt bekommt hier zwei bis drei davon, und jedes Kriterium hat einen Testfall, der zeigt, wie man es prüft. Die Kriterien sagen, was ein Nutzer oder Betreiber erlebt. Wie es gebaut wird, entscheiden die Issues.

Die Seite ist ein Entwurf. Ausgearbeitet sind bisher die Kernpunkte, an denen M2 arbeitet, die übrigen folgen. Kriterien für spätere Meilensteine sind gröber und werden im Meilenstein davor konkretisiert. Welche Kernpunkte es gibt und was jeder bewusst nicht baut, steht in den [Nicht-Zielen](non-goals.md). Die Überschriften hier heißen genauso wie dort, `AcceptanceTests` in `Edvaniq.ArchitectureTests` hält beide Seiten gleich.

Der Testfall steht in der Form Gegeben, Wenn, Dann. „Messweise“ nennt die Art des Tests und das Issue, das ihn liefert. „Ab“ nennt den Meilenstein, ab dem das Kriterium gilt, von da an zählt eine Verletzung als Fehler. Grenzen, Limits und Fristen stehen mit ihrer ID in den [Qualitätsanforderungen](quality.md), etwa Q6, und hier nur als Verweis. Die IDs A1, A2 und so weiter zählen über die ganze Seite und werden nicht neu vergeben.

## Kernpunkte der MVP-Liste

Die Reihenfolge folgt der MVP-Liste.

### MVP1 Konto und Anmeldung

| ID | Kriterium | Testfall | Messweise | Ab |
|---|---|---|---|---|
| A1 | Ein Konto entsteht nur mit einem gültigen Einladungscode, und jeder Code lässt sich genau einmal einlösen. | Gegeben ein offener Code. Wenn sich zwei Personen gleichzeitig damit registrieren, dann entsteht genau ein Konto, und die andere Person bekommt einen verständlichen Hinweis. Ohne Code und mit einem widerrufenen Code wird die Registrierung ebenso abgewiesen. | Integrationstest in Identity (#249), Negativfall im E2E-Test (#334) | M2 |
| A2 | Erst die Bestätigungsmail macht ein Konto nutzbar. Danach meldet sich der Nutzer an und ab, und wiederholte Fehlversuche sperren das Konto vorübergehend. | Gegeben ein frisch registriertes Konto eines sicher volljährigen Nutzers. Wenn er sich vor der Bestätigung anmeldet, dann wird er abgewiesen. Nach dem Klick auf den Link aus der Mail gelingt die Anmeldung, und nach dem Logout leiten geschützte Seiten wieder zum Login. Nach der festgelegten Zahl von Fehlversuchen schlägt auch das richtige Passwort fehl, bis die Sperre abläuft. | Integrationstests (#250, #251, #252), E2E-Test (#334) | M2 |
| A3 | Ein Nutzer setzt sein Passwort per Mail zurück oder ändert es, danach gilt nur das neue. Ohne das richtige Passwort verrät keine Antwort, ob eine E-Mail-Adresse registriert ist. | Gegeben ein bestätigtes Konto eines sicher volljährigen Nutzers. Wenn er einen Reset anfordert und den Link aus der Mail benutzt, dann meldet er sich nur noch mit dem neuen Passwort an, und ein zweiter Klick auf den Link schlägt fehl. Eine Änderung ohne das aktuelle Passwort wird abgewiesen. Registrierung, Anmeldung mit falschem Passwort und Reset-Anfrage geben für seine und für eine unbekannte Adresse dieselbe Rückmeldung. | Integrationstests (#253, #254, #265) | M2 |

### MVP2 Lernziel anlegen

| ID | Kriterium | Testfall | Messweise | Ab |
|---|---|---|---|---|
| A4 | Der Assistent speichert ein Lernziel mit allen Feldern und erklärt ungültige Eingaben am Feld. | Gegeben ein angemeldeter Nutzer. Wenn er im Assistenten Name, Art, Prüfungsart, Prüfungstermin, Stufe und Ausgabesprache eingibt und speichert, dann hat das gespeicherte Lernziel genau diese Werte, und die Lernziel-Übersicht zeigt es mit Name, Art und Prüfungstermin. Ein leerer Name und ein Prüfungstermin in der Vergangenheit werden mit Hinweis am Feld abgewiesen. | Integrationstest in Planning (#272), Komponententests (#273, #281) | M2 |
| A5 | Im Assistenten eingegebene Themen stehen im Wissensbaum des Lernziels, ohne doppelte Einträge. | Gegeben ein neues Lernziel, für das der Nutzer „Bruchrechnung“ und zweimal „Prozentrechnung“ eingegeben hat. Wenn er den Wissensbaum öffnet, bevor ein Dokument analysiert ist, dann stehen dort genau diese zwei Themen. | Integrationstest (#317) | M2 |
| A6 | Der Upload-Schritt im Assistenten lässt sich überspringen und später nachholen. | Gegeben der Assistent direkt nach dem Speichern des Lernziels. Wenn der Nutzer den Upload überspringt, dann landet er auf der Lernziel-Übersicht mit einem Hinweis zum nächsten Schritt und kann dort später Material hinzufügen. | Komponententest (#280) | M2 |

### MVP3 Art des Lernziels

| ID | Kriterium | Testfall | Messweise | Ab |
|---|---|---|---|---|
| A7 | Die Art bestimmt, welche Felder der Assistent verlangt. | Gegeben der Assistent. Wenn der Nutzer Prüfungsvorbereitung wählt, dann verlangt der Assistent eine Prüfungsart. Wählt er Freies Lernen, dann fragt der Assistent weder Prüfungsart noch Prüfungstermin ab. Beide Lernziele lassen sich speichern. | Integrationstest (#272), Komponententest (#273) | M2 |
| A8 | Freies Lernen mit eigenem Material läuft wie die Prüfungsvorbereitung. | Gegeben zwei Nutzer mit je einem Lernziel derselben Stufe und Ausgabesprache, einer mit Prüfungsvorbereitung und einer mit Freiem Lernen, und dasselbe PDF bei beiden. Wenn beide Dokumente analysiert werden, dann bekommt der simulierte KI-Anbieter für beide denselben Prompt. | Integrationstest in Content (#323) | M2 |
| A9 | Ohne Material erzeugt die KI ein Themengerüst, das erst nach Bestätigung durch den Nutzer gilt. | Gegeben ein Lernziel der Art Freies Lernen ohne Dokument. Wenn der Nutzer ein Themengerüst anfordert, dann sieht er einen Vorschlag in zwei Ebenen, den er ändern kann. In den Wissensbaum kommt es erst, wenn er bestätigt. | Integrationstest (#560), Komponententest (#562) | M5 |

### MVP4 Material hochladen

| ID | Kriterium | Testfall | Messweise | Ab |
|---|---|---|---|---|
| A10 | Die erlaubten Formate kommen an, alles andere lehnt die App mit Grund ab. Ohne die Bestätigung „nur eigenes Material“ nimmt sie nichts an. | Gegeben ein Lernziel. Wenn der Nutzer ein Dokument ohne die Bestätigung hinzufügt, dann weist die App es ab. Mit Bestätigung steht je ein PDF mit Textlayer, DOCX, TXT, Markdown und ein eingefügter Text als Dokument mit dem Status „hochgeladen“ in der Materialliste. Eine Präsentation, ein Bild und ein Bild mit der Endung `.pdf` werden mit verständlichem Hinweis abgelehnt. | Integrationstests in Content (#275, #276, #278) | M2 |
| A11 | Die festen Grenzen greifen mit einer Meldung, die Grenze und Ausweg nennt. | Gegeben ein Lernziel, das die Zahl der Dokumente aus Q6 erreicht hat, und eines ohne Dokument. Wenn der Nutzer dem ersten ein weiteres Dokument hinzufügt und dem zweiten eine Datei über der Größen- oder der Seitengrenze aus Q6, dann lehnt die App jeweils ab, nennt die Grenze und sagt, was er tun kann. | Integrationstests (#277), Upload über die öffentliche Domain (#319) | M2 |
| A12 | Ein gescanntes PDF ohne Text erkennt die App und lehnt es ab, ohne die KI zu fragen. | Gegeben das Scan-Dokument aus dem Referenzset. Wenn der Nutzer es hochlädt, dann endet es als „fehlgeschlagen“ mit einem Hinweis, eine Textversion zu nehmen oder den Text einzufügen. Das Kostenlogging zeigt keinen KI-Aufruf dafür. | Integrationstest (#284) | M2 |

### MVP5 Inhaltsanalyse

| ID | Kriterium | Testfall | Messweise | Ab |
|---|---|---|---|---|
| A13 | Aus jedem Dokument entstehen Abschnitte mit Quelle, Themen in zwei Ebenen und Kernbegriffe mit Definition, und jeder Abschnitt gehört zu einem Thema. | Gegeben ein PDF mit Textlayer aus dem Referenzset. Wenn die Analyse fertig ist, dann ist das Dokument „bereit“, jeder Abschnitt nennt Dokument und Seite und gehört zu einem Thema, und die Themen haben höchstens zwei Ebenen. Jeder Kernbegriff hat eine Definition und verweist auf seinen Abschnitt. | Integrationstests mit dem simulierten Anbieter (#289, #291, #292, #293), Referenzset gegen den echten Anbieter (#309) | M2 |
| A14 | Ein weiteres Dokument ergänzt den Wissensbaum nur. | Gegeben ein Lernziel, dessen erstes Dokument analysiert ist. Wenn ein zweites Dokument zum selben Stoff analysiert wird, dann behalten alle bisherigen Themen ID, Namen und Platz. Passende Abschnitte kommen zu vorhandenen Themen, und nur Neues wird ein neues Thema. | Integrationstest (#308) | M2 |
| A15 | Eine gescheiterte Analyse hinterlässt nichts Halbes, nennt ihren Grund und lässt sich wiederholen. | Gegeben eine Analyse, die abbricht, weil der simulierte KI-Anbieter nicht antwortet. Wenn der Nutzer die Materialliste öffnet, dann steht dort „fehlgeschlagen“ mit dem Grund „KI derzeit nicht verfügbar“, und kein Thema aus dem Abbruch ist zu sehen. Nach einem Retry mit erreichbarem Anbieter ist das Dokument „bereit“, ohne doppelte Themen. | Integrationstests (#300, #322), Komponententest der Materialliste (#285) | M2 |

### MVP27 Wissensbaum

In M2 zeigt der Wissensbaum Themen, Kernbegriffe und Quellen, Mastery und Themenzustand kommen in M4 dazu.

| ID | Kriterium | Testfall | Messweise | Ab |
|---|---|---|---|---|
| A16 | Der Wissensbaum zeigt zwei Ebenen mit Kernbegriffen und Quellen und sagt, dass die Themen von der KI kommen. | Gegeben ein analysiertes Dokument. Wenn der Nutzer den Wissensbaum öffnet, dann kann er jedes Thema aufklappen und sieht seine Unterthemen, die Kernbegriffe mit Definition und die Abschnitte mit Dokument und Seite. Ein Hinweis sagt, dass die KI die Themen vorschlägt und er sie prüfen soll. | Komponententest (#294) | M2 |
| A17 | Umbenennen, Ausblenden und Hinzufügen überstehen jede spätere Analyse. | Gegeben ein Thema, das der Nutzer umbenannt hat, eines, das er ausgeblendet hat, und eines, das er hinzugefügt hat. Wenn dasselbe Dokument noch einmal und ein weiteres neu analysiert wird, dann behält das umbenannte Thema seine ID und den Namen des Nutzers, das ausgeblendete bleibt ausgeblendet, und das hinzugefügte bleibt erhalten. | Integrationstest in der CI (#327) | M2 |
| A18 | Je Thema zeigt der Wissensbaum Mastery und Themenzustand und führt zu Zusammenfassung, Karten und Quiz. | Gegeben ein Thema mit Lern-Nachweisen. Wenn der Nutzer den Wissensbaum öffnet, dann zeigt das Thema seine Mastery als Balken und seinen Themenzustand nicht nur als Farbe, und von dort erreicht er Zusammenfassung, Karten und Quiz des Themas. | Komponententests (#468, #469) | M4 |

## Ergänzungen

Die Ergänzungen stehen in der Reihenfolge der Roadmap. Altersangabe und Zustimmung aus MVP1 stehen bei Recht, Datenschutz und Jugendschutz.

### Sicherheits-Basis

| ID | Kriterium | Testfall | Messweise | Ab |
|---|---|---|---|---|
| A19 | Kein Nutzer liest, ändert oder löscht Daten eines anderen. | Gegeben die Nutzer A und B mit je einem Lernziel, einem Dokument und einem Thema. Wenn B jeden Endpunkt mit den IDs von A aufruft, dann bekommt er dieselbe Antwort wie für eine ID, die es nicht gibt, und die Daten von A bleiben unverändert. | `IsolationTests` in jedem aktiven Service (#270, #271), Q30 | M0 |
| A20 | Skripte und Markup in Texten von Nutzern und von der KI werden nie ausgeführt. Dargestellt wird reiner Text, ab M3 bereinigtes Markdown. | Gegeben ein Dateiname, ein Pseudonym, ein Lernziel-Name, ein Thema und ein Kernbegriff mit eingebettetem Skript und Markup, Thema und Kernbegriff aus einer Ausgabe des simulierten KI-Anbieters. Wenn der Nutzer jede Ansicht von M2 öffnet, dann stehen sie dort als Text, und kein Skript läuft. | Test in der CI (#316) | M2 |
| A21 | Jeder Service arbeitet mit eigenem Datenbank-Benutzer in seiner eigenen Datenbank, und Verwaltungswerkzeuge sind aus dem Internet nicht erreichbar. | Gegeben das App-Modell des AppHost und der ausgerollte Stand. Wenn man prüft, welche Datenbank jeder Service bekommt, dann ist es seine eigene mit eigenem Benutzer. Ein Aufruf über die öffentliche Domain erreicht kein Verwaltungswerkzeug. | `Rule1DatabaseTests`, Prüfung auf dem Server (#163) | M0 |

### KI-Plattform und Kostenkontrolle

Wie viel die KI kosten darf und welche Limits gelten, steht in Q21 bis Q24.

| ID | Kriterium | Testfall | Messweise | Ab |
|---|---|---|---|---|
| A22 | Jeder KI-Aufruf liefert eine gültige und moderierte strukturierte Ausgabe oder einen kontrollierten Fehler, nie halbe Daten. | Gegeben ein simulierter Anbieter, der erst eine ungültige und dann eine gültige Ausgabe liefert, einer, der nur ungültige liefert, und ein Dokument mit unangemessenem Inhalt. Wenn je eine Analyse läuft, dann speichert die App im ersten Fall das gültige Ergebnis. Im zweiten und dritten Fall markiert sie das Dokument als fehlgeschlagen mit Grund und speichert nichts davon. Jeder Versuch steht mit seinen Kosten im Kostenlogging. | Tests mit dem simulierten Anbieter (#288, #298, #299) | M2 |
| A23 | Das Tageslimit an Seiten und das Monatslimit stoppen neue KI-Aufträge mit verständlicher Meldung. | Gegeben ein Nutzer, der sein Tageslimit an Seiten aus Q23 ausgeschöpft hat. Wenn er ein weiteres Dokument hochlädt, dann lehnt die App es ab und nennt den Zeitpunkt der Rücksetzung, während ein anderer Nutzer weiter analysieren kann. Ist das Monatslimit erreicht, lehnt sie neue KI-Aufträge aller Nutzer ab. | Automatisierte Tests (#301, #303) | M2 |
| A24 | Der Not-Aus stoppt alle KI-Aufrufe ohne Deployment, der Rest der App bleibt nutzbar. | Gegeben eine laufende Analyse. Wenn ein Betreiber den Not-Aus einschaltet, dann endet sie als fehlgeschlagen mit Retry, und neue Analysen zeigen „KI derzeit pausiert“. Login, Lernziele und Listen funktionieren weiter. Nach dem Ausschalten gelingt der Retry. | Automatisierter Test (#305), Probe im Live-System (#606) | M2 |

### Recht, Datenschutz und Jugendschutz

Dass Löschen die Daten aus allen Services entfernt, misst Q28.

| ID | Kriterium | Testfall | Messweise | Ab |
|---|---|---|---|---|
| A25 | Die Rechtstexte sind von jeder Seite aus erreichbar, und die Zustimmung bei der Registrierung verweist auf die Versionen, die dabei galten. | Gegeben jeweils Version 1 von Nutzungsbedingungen und Datenschutzerklärung. Wenn jemand ohne Anmeldung eine beliebige Seite öffnet, dann verlinkt sie Impressum, Datenschutzerklärung mit kindgerechter Kurzfassung und Nutzungsbedingungen. Registriert sich ein Nutzer und erscheint danach Version 2 der Datenschutzerklärung, dann steht in seiner Zustimmung Version 1 mit Zeitpunkt, und Version 1 ist unter ihrer festen Adresse weiter abrufbar. Ohne Zustimmung ist keine Registrierung möglich. | Integrationstest (#261), Komponententest (#260) | M2 |
| A26 | Wer minderjährig sein kann, meldet sich erst nach der Freigabe durch den Betreiber an. | Gegeben zwei bestätigte Konten, eines mit einem Geburtsjahr, bei dem der Nutzer sicher volljährig ist, und eines, bei dem er noch 17 sein kann. Wenn sich beide anmelden, dann gelingt es dem ersten sofort, und das zweite bekommt einen kindgerechten Hinweis zum Freigabeverfahren. Nach der Freigabe gelingt auch seine Anmeldung. | Integrationstest (#256), Login-Hinweis (#269), Minderjährigen-Fall in der Abnahme (#335) | M2 |
| A27 | An den KI-Anbieter gehen keine personenbezogenen Daten. | Gegeben ein Testkonto mit erkennbarer E-Mail-Adresse, Pseudonym, Nutzer-ID und Lernziel-Namen. Wenn der Nutzer dieses Kontos Material hochlädt und analysieren lässt, dann enthält kein Prompt an den simulierten Anbieter einen dieser Werte. | Test in der CI (#312) | M2 |