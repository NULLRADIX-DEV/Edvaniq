# Qualitätsanforderungen

Eine Anforderung, die sich nicht messen lässt, kann niemand abnehmen. Deshalb hat jedes Qualitätsziel hier einen Zielwert und eine Messweise, also einen Test, einen CI-Check, eine Messung oder eine Prüfung von Hand. Die Spalte „Ab“ nennt den Meilenstein, ab dem der Wert gilt. Von da an zählt eine Verletzung als Fehler.

Eine Nummer wie #230 nennt das Issue, das die Prüfung liefert, solange es sie noch nicht gibt. Steht hinter einem Wert „(Vorschlag)“, hat das Team ihn noch nicht bestätigt, und er gilt erst danach. Die Werte gelten für den MVP-Kern. Tutor, Foto-Upload und Gamification gehören zum MVP-Plus und bekommen ihre Werte, wenn sie eingeschaltet werden.

## Antwortzeit und Verarbeitung

Die Antwortzeiten gelten für Anfragen ohne KI im Antwortweg und ohne die Strecke zwischen Browser und Server. Die Analyse eines Dokuments läuft im Hintergrund, der Nutzer sieht ihren Status. Nur die Erstladezeit in Q7 misst die ganze Strecke.

| ID | Ziel | Zielwert | Messweise | Ab |
|---|---|---|---|---|
| Q1 | Abfragen zwischen Services und Lesezugriffe der Clients antworten schnell. | 500 ms | Zeitmessung an der Abfrage der Mastery mit 50 Themen (#209, #364) und der fälligen Karten mit 500 Karten (#447). Im Lasttest gilt der Wert für 95 % der Anfragen (#579). | M1 |
| Q2 | Eine Aktion im Kernfluss wie Antwort speichern, Plan laden oder Session starten antwortet schnell. | 1 s für 95 % der Aktionen am Web-Host (Vorschlag) | Lasttest-Stichprobe mit einer Analyse und 5 parallelen Sessions (#415), danach Lasttest mit simulierten Pilotnutzern (#579). | M3 |
| Q3 | Der Plan zieht nach einer Session schnell nach. | 2 s für die Neuberechnung bei Demo-Umfang | Zeitmessung in dem Test, der nach der Demo-Session den Restplan neu berechnet (#378). | M3 |
| Q4 | Mastery und Tagesplan erscheinen nach einer Antwort ohne manuelles Neuladen, obwohl sie über Events asynchron ankommen. | 5 s typische Verzögerung | Messung im Durchstich, der Wert wird im Repo dokumentiert (#222). | M1 |
| Q5 | Ein hochgeladenes Dokument wird analysiert, ohne dass der Nutzer dabei warten muss. | 10 Minuten bis „bereit“ bei 100 Seiten (Vorschlag) | Der Referenzlauf mit dem echten Anbieter (#309) hält die Dauer je Dokument zusätzlich fest, dazu der Lasttest (#579). Dass der Upload sofort zurückkehrt, prüft #289. | M2 |
| Q6 | Uploads haben feste Grenzen, und der Nutzer erfährt verständlich, wenn er sie überschreitet. | 20 MB und 100 Seiten je Datei, 10 Dokumente je Lernziel | Tests für beide Umfangsgrenzen (#277). Upload einer 20-MB-Datei über die öffentliche Domain und einer größeren, die früh abgewiesen wird (#319, #275). | M2 |
| Q7 | Die Web-Shell lädt beim ersten Aufruf schnell, auch auf dem Smartphone. | 5 s bis zur bedienbaren Shell, ohne Cache und mit gedrosseltem Netz (Vorschlag) | Messung mit den Entwicklerwerkzeugen des Browsers im Profil „Slow 4G“, Teil der Geräte-Abnahme (#611). | M6 |

## Verfügbarkeit und Wiederherstellung

Edvaniq läuft auf einem einzelnen Server. Deshalb zählt, wie schnell ein Ausfall auffällt und wie schnell ein Stand zurückkehrt, mehr als eine Doppelung der Technik.

| ID | Ziel | Zielwert | Messweise | Ab |
|---|---|---|---|---|
| Q8 | Die Web-App ist erreichbar. | 99,0 % je Kalendermonat, also höchstens rund 7 Stunden Ausfall (Vorschlag) | Externer Uptime-Check von außerhalb des Servers, Auswertung je Monat (#230). Nach jedem Deploy prüft zusätzlich `deploy/smoke-test.sh`, ob Shell und Ping antworten. | M1 |
| Q9 | Ein Ausfall fällt dem Team auf, bevor ein Nutzer ihn meldet. | 5 Minuten bis zum Alarm (Vorschlag) | Ein absichtlicher Ausfall wird erkannt (#230). Der Ausfall eines Prozesses oder des Brokers alarmiert ebenfalls (#234). | M1 |
| Q10 | Ein kaputter Stand lässt sich zurücknehmen. | 15 Minuten | Rollback über den Workflow „Deploy“, einmal auf dem Server erprobt (#127). Nach einem roten Smoke-Test rollt der Workflow selbst zurück ([Deploy](../deploy.md#hat-es-geklappt)). | M0 |
| Q11 | Ein fehlgeschlagenes Deployment lässt sich samt Datenbanken auf den letzten getaggten Stand zurücksetzen. | 30 Minuten | Probelauf vor der Demo: Stand und Datenbanken kommen zurück, danach ist der Kernfluss-Test grün (#421). | M3 |
| Q12 | Fällt der Server komplett aus, geht höchstens ein Tag an Daten verloren. | 24 Stunden, folgt aus der täglichen Sicherung | Tägliche verschlüsselte Sicherung außerhalb des Servers (#227), Alarm bei einer fehlgeschlagenen Sicherung (#232). Vor jeder Migration entsteht zusätzlich eine Sicherung. | M1 |
| Q13 | Eine Datenbank lässt sich aus der Sicherung wiederherstellen. | 4 Stunden (Vorschlag) | Restore-Test mit gemessener Dauer (#229). Vor dem Pilot folgt der Restore aus der Sicherung außerhalb des Servers für Datenbanken und Uploads (#582). | M1 |

## Speicher und Last

Die Obergrenze für die ganze App und die Grenzen je Container stehen in `deploy/compose.yml` und im Abschnitt [Speicher](../deploy.md#speicher) der Deploy-Doku. Hier steht nur, was daran gemessen wird.

| ID | Ziel | Zielwert | Messweise | Ab |
|---|---|---|---|---|
| Q14 | Die ganze App bleibt in ihrem Speicherbudget, und jeder Container hat eine feste Grenze ohne Swap. | 6 GB als Summe aller Grenzen | Summe der `mem_limit` in `deploy/compose.yml`, im Review jeder Änderung daran von Hand geprüft. Der Server lehnt eine Compose-Datei ohne Grenzen ab. | M0 |
| Q15 | Jeder Container hat im Normalbetrieb Luft nach oben. | höchstens 80 % seiner Grenze (Vorschlag) | RAM-Messung je Container im Ruhezustand (#165) und unter Durchstich-Last (#243), erneut nach dem siebten Service (#502) und nach der Abstimmung der Grenzen vor dem Pilot (#576). | M0 |
| Q16 | Die App hält Last aus, ohne dass ein Container wegen Speichermangel beendet wird. | 5 parallele Sessions neben einer Analyse, später 20 gleichzeitig aktive Nutzer (Vorschlag), jeweils 0 Beendigungen durch Speichermangel | Lasttest-Stichprobe (#415). Danach ein wiederholbares Szenario mit simulierten Pilotnutzern und KI-Test-Double (#578, #579), dessen Nutzerzahl aus der Pilotplanung kommt. | M3 |
| Q17 | Der Plattenplatz reicht, und das Team erfährt früh, wenn er knapp wird. | Alarm bei 80 % belegtem Platz (Vorschlag) | Testweise ausgelöster Alarm (#231). Das Logvolumen je Container ist begrenzt und wird rotiert (#167). | M1 |

## Barrierefreiheit

Ziel ist WCAG 2.2 Stufe AA. Verbindlich für den Pilot ist die Basis-Barrierefreiheit aus Q19 und Q20: Kontrast, Tastatur, Beschriftung für Screenreader und reduzierte Bewegung. Darüber hinaus prüft der automatische Check die AA-Kriterien, die sich automatisch erkennen lassen.

| ID | Ziel | Zielwert | Messweise | Ab |
|---|---|---|---|---|
| Q18 | Die Kernansichten erfüllen WCAG 2.2 Stufe AA. | 0 offene Verstöße gegen AA in den Kernansichten | Automatischer Check der Shell in der CI (#147), Prüfprotokolle je Meilenstein (#296, #425, #509), Audit mit Screenreader vor dem Pilot (#568), Abnahme (#611). | M0 |
| Q19 | Text und Bedienelemente haben genug Kontrast. | 4,5:1 für Text, 3:1 für Rahmen und Bedienelemente | `ContrastTests` in `tests/Clients` rechnet jedes Farbpaar der Tokens in `edvaniq.css` nach. Eine neue Farbe bekommt dort ihr Paar. | M0 |
| Q20 | Jede Kernansicht lässt sich allein mit der Tastatur bedienen, ist für Screenreader beschriftet und stoppt Animationen, wenn das System reduzierte Bewegung verlangt. | 100 % der Kernansichten | Checkliste in der Definition of Done der UI-Bibliothek (#259), Prüfprotokolle je Meilenstein (#296, #425, #509), Stichprobe je Kernansicht in der Abnahme (#611). | M2 |

## KI-Kosten und Limits

Die KI ist der einzige Kostenpunkt, der mit der Nutzerzahl wächst. Deshalb stehen die Ziele hier je Nutzer, begrenzt durch Tageslimits, ein Monatslimit und ein hartes Limit beim KI-Anbieter. Die Höhe des Monatslimits und das Gesamtbudget stehen nicht im Repo.

| ID | Ziel | Zielwert | Messweise | Ab |
|---|---|---|---|---|
| Q21 | KI-Kosten bleiben je Nutzer klein. Aktiv ist, wer im Kalendermonat mindestens eine Session abschließt. | 1 € im Durchschnitt je aktivem Nutzer und Monat (Vorschlag) | Token- und Kostenlogging je Aufruf (#299), im Pilot ein wöchentlicher Kostenbericht mit Kosten gesamt und je Nutzer (#634). | M2 |
| Q22 | Ein einzelner Nutzer verursacht auch dann kaum Kosten, wenn er seine Limits jeden Tag ausschöpft. | 3 € je Nutzer und Monat im Höchstfall (Vorschlag) | Rechnung aus den Tageslimits und den gemessenen Kosten je Dokument und je Fragenpool. Liegt der Höchstfall darüber, werden die Limits gesenkt (#311). | M2 |
| Q23 | Tageslimits je Nutzer begrenzen die KI-Nutzung. | 200 verarbeitete Seiten und 20 Generierungen je Tag (Vorschlag), Tageswechsel um 04:00 Uhr Europe/Berlin | Je Limit ein automatisierter Test: Wer es überschreitet, bekommt eine Meldung mit dem Zeitpunkt der Rücksetzung (#301, #302). Die Werte lassen sich ohne Codeänderung anpassen (#311). | M2 |
| Q24 | Eine Kostenbremse stoppt die Ausgaben, bevor sie das Budget sprengen. | Warnung an das Team bei 80 % des Monatslimits, ab 100 % lehnt die App neue KI-Aufträge ab | Test des Monatslimits (#303), Ausgabenlimit beim Anbieter über dem Monatslimit (#304), Not-Aus ohne Deployment (#305), Not-Aus im Live-System geprüft (#606). | M2 |

## Datenaufbewahrung und Löschung

Jede Datenart hat eine Frist, nach der sie verschwindet. Löscht ein Nutzer Dokument, Lernziel oder Konto, verschwinden die Daten aus allen Services (Q28). Aus Logs und Sicherungen gehen sie erst, wenn deren Frist aus Q25 und Q26 abläuft.

| ID | Ziel | Zielwert | Messweise | Ab |
|---|---|---|---|---|
| Q25 | Logs verschwinden nach kurzer Zeit und enthalten nichts Persönliches. | 14 Tage Aufbewahrung | Stichprobe im Log-Bestand auf ältere Einträge und Nutzerinhalte (#545). Eine Prüfung in der CI sucht nach Passwörtern, Token, Prompts und Materialtexten (#314). | M1 |
| Q26 | Sicherungen verschwinden nach begrenzter Zeit. | höchstens 30 Tage | Das Entfernen älterer Sicherungen gehört zur Sicherung (#227), eine Stichprobe belegt es (#545). | M1 |
| Q27 | Lern-Nachweise werden nicht unbegrenzt gespeichert. | 12 Monate nach der Antwort (Vorschlag) | Frist samt Begründung im Vertrag der Lern-Nachweise (#190). Test mit verkürzter Frist: Die Nachweise verschwinden, die Mastery bleibt konsistent (#365). | M1 |
| Q28 | Löschen von Dokument, Lernziel oder Konto entfernt die Daten in jedem aktiven Service. | 24 Stunden bis zur Bestätigung aller Services (Vorschlag) | Integrationstests je Service (#240), später ein Test über alle Services mit den Daten von zwei Nutzern (#544). Der Löschstatus wird bis zur Bestätigung nachverfolgt (#535). | M1 |
| Q29 | Auskunft und Export gehen auf Anfrage raus. | 1 Monat | Der Prozess ist dokumentiert (#537) und mit einem Testkonto einmal durchlaufen, die Bearbeitungsdauer ist festgehalten (#574). | M5 |

## Sicherheit

Diese drei Werte prüft die CI heute schon bei jedem Pull Request. Sie stehen hier, damit sie Teil jeder Abnahme bleiben.

| ID | Ziel | Zielwert | Messweise | Ab |
|---|---|---|---|---|
| Q30 | Ein Nutzer sieht nur seine eigenen Daten. | 0 fremde Datensätze in jeder Antwort | Test „Nutzer A sieht B nicht“ in `IsolationTests`, schon in der Vorlage und in Planning. Jeder weitere Service und jede neue Funktion bekommt ihn (#270, #504, #586). | M0 |
| Q31 | Kein Secret landet im Repo. | 0 Funde | Der CI-Job „Secret scan“ durchsucht bei jedem Pull Request die ganze Git-History ([Secrets](../secrets.md#wie-geprüft-wird)). | M0 |
| Q32 | Ausgelieferte Pakete und Images haben keine bekannte schwere Lücke. | 0 Funde ab high bei Paketen, 0 behebbare Funde ab critical im Basis-Image des Web-Hosts, im Wochenlauf ab high | NuGet-Audit beim Restore und Grype in der CI ([Abhängigkeiten](../dependencies.md#was-jeder-pull-request-prüft)). | M0 |

## Warum die Werte zum Server und zum Budget passen

Die App darf höchstens 6 GB belegen, und die Summe der Container-Grenzen liegt heute darunter ([Speicher](../deploy.md#speicher)). Das reicht, weil auf dem Server nur Backend und Datenbank laufen. Die KI kommt von einem externen Anbieter, es laufen weder ein Modell noch eine Texterkennung noch eine Vektordatenbank. Ein Prozess braucht also nur den Speicher für seine Anfragen, und die 80 % aus Q15 lassen Platz für Spitzen.

Die Anfragen im Kernfluss sind kurz. Eine Abfrage liest die Daten eines Nutzers aus seiner eigenen Datenbank, bei 50 Themen oder 500 Karten sind das wenige Zeilen, und 500 ms lassen reichlich Luft. Schwer ist nur die Analyse eines Dokuments. Sie läuft im Content-Worker mit begrenzter Parallelität, sodass ein Nutzer die anderen nicht ausbremst, und die 10 Minuten aus Q5 lassen Platz für mehrere Dokumente hintereinander.

Ein Monat mit 99 % erlaubt gut sieben Stunden Ausfall. Das passt zu einem kleinen Team ohne Bereitschaftsdienst: Der schnelle Alarm (Q9) und der schnelle Rollback (Q10) bringen mehr als ein zweiter Server. Der Server kostet gleich viel, egal wie viele Nutzer kommen. Mit der Nutzerzahl wachsen nur die KI-Kosten, und die sind je Nutzer (Q21, Q22), je Tag (Q23) und insgesamt (Q24) gedeckelt. Aus den Tageslimits und den gemessenen Kosten je Dokument rechnet #311 nach, ob der Pilot unter dem Monatslimit bleibt. Barrierefreiheit braucht keinen Betrieb, sie hängt an der Definition of Done der UI. Die Fristen für Logs und Sicherungen halten den Plattenplatz klein, denn beides wächst nur 14 bzw. 30 Tage.

Gesamtbudget, Serverpreise und Verträge stehen bewusst nicht in diesem Repo.
