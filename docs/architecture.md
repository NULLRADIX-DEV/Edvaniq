# Architektur

Edvaniq besteht aus Microservices, die .NET Aspire zusammenhält. Jeder Service ist ein eigener Bounded Context: Er hat seine eigene Datenbank, wird für sich gebaut und läuft als eigener Prozess. So kann ein Service wachsen oder ausgetauscht werden, ohne dass die anderen es merken.

## Ordnerstruktur

```
src/
  Aspire/          AppHost (lokale Orchestrierung), ServiceDefaults (Telemetrie, Health, Service Discovery)
  BuildingBlocks/  technische Basisbibliotheken, keine Fachlogik
  Gateway/         API-Gateway (YARP), einziger Einstiegspunkt für Clients
  Services/<Name>/ ein Microservice = Api + Application + Domain + Infrastructure + Contracts (+ Worker)
  Clients/         Client.Core, Client.DesignSystem, Client.UI, Web, Web.Client, App (MAUI)
tests/
  Gateway/         IntegrationTests des Gateways
  Services/<Name>/ UnitTests + IntegrationTests pro Service
  Edvaniq.ArchitectureTests, Edvaniq.EndToEndTests, Edvaniq.Testing (geteilte Test-Helfer)
templates/
  service/         Vorlage für neue Services (dotnet new edvaniq-service)
```

## Services

| Service | Verantwortung |
|---|---|
| Identity | Account, Profil, Einstellungen, Lernpräferenzen, DSGVO |
| Planning | Lernziele, Lernpfade, Meilensteine, Lernplan, Tagesplan, Kalender |
| Content | Upload, Extraktion/OCR, Dokumentanalyse, Zusammenfassungen, Notizen, Semantic Search (Worker: Ingestion) |
| Knowledge | Themen, Wissensbaum, Abhängigkeiten, Wissensgraph |
| Assessment | Einstufungstest, Quiz, Fragegenerierung, Prüfungsmodus, Fehleranalyse |
| Flashcards | Karteikarten, Spaced Repetition, Import/Export |
| LearningEngine | Lernermodell: Mastery, Forgetting, Difficulty, Next Best Action, Session Generator |
| Tutor | KI-Tutor, adaptive Erklärungen, Active Recall/Feynman |
| Gamification | XP, Level, Streaks, Goals, Achievements, Quests |
| Analytics | Statistiken, Lernzeit-Analyse, Wochenrückblick |
| Notifications | Push, Erinnerungen (Worker: Scheduler/Versand) |

## Schichten eines Service

| Projekt | Inhalt | darf referenzieren |
|---|---|---|
| `.Domain` | Entities, Aggregates, Value Objects, Domain Events, Geschäftsregeln | BuildingBlocks.Domain |
| `.Application` | Use Cases (Commands/Queries + Handler), Ports (Interfaces), Integration-Event-Handler | eigene Domain + Contracts, BuildingBlocks.Application, fremde `.Contracts` |
| `.Infrastructure` | EF Core DbContext/Migrationen, Repositories, Messaging, KI-Provider, externe APIs | Application, BuildingBlocks.Infrastructure/Messaging/AI |
| `.Api` | Minimal-API-Endpoints, DI-Komposition, Startpunkt | Application, Infrastructure, BuildingBlocks.Web, ServiceDefaults |
| `.Contracts` | Integration Events (öffentliche Schnittstelle zu anderen Services) | BuildingBlocks.Contracts |
| `.Worker` | Hintergrundverarbeitung (nur Content, Notifications) | Application, Infrastructure, ServiceDefaults |

Die Abhängigkeiten zeigen immer nach innen: Api, dann Infrastructure, dann Application, dann Domain. Die Fachlogik weiß also nichts von Datenbank oder HTTP.

## Service-Regeln

Diese sechs Regeln halten die Services auseinander. Zu jeder gehört eine Prüffrage, deren Antwort Nein sein muss. Unter „Test“ steht, was die Architekturtests in `Edvaniq.ArchitectureTests` dazu prüfen, mit der Testklasse. Zu jeder Prüfung gehört dort ein Test, der einen Fake-Service mit genau diesem Verstoß anlegt und rot erwartet. Was ein Test nicht sehen kann, steht dabei und gehört ins Review.

1. **Eigene Datenbank.** Jeder Service mit Daten hat genau eine eigene Datenbank mit eigenem DB-Benutzer. Ihren Connection String bekommen nur die Prozesse dieses Service: lokal über `WithDatabase` im AppHost, auf dem Server über `<name>.env` in `deploy/compose.yml`.
   - Prüffrage: Bekommt ein Prozess die Datenbank eines anderen Service?
   - Test: `Rule1DatabaseTests` baut das Modell des AppHost, ohne es zu starten: Jeder Prozess bekommt höchstens eine Datenbank, nur die seines Service, und nie den ganzen Server. Seine `<name>.env` in `deploy/compose.yml` hängt nur an `<name>` und `<name>-*`, das prüft die CI im Job „Container images“. Dass MySQL einem Benutzer jeden Zugriff außerhalb seiner Datenbank verweigert, belegt `DatabaseUserTests` in den Integrationstests jedes Service, die Vorlage bringt ihn mit. Er legt zwei Datenbanken mit je eigenem Benutzer an, mit demselben `GRANT` wie der AppHost, und prüft gegen eine echte MySQL: Die fremde Datenbank lässt sich weder lesen noch ändern noch auflisten, und `CREATE DATABASE` scheitert.
2. **Nur Contracts fremder Services.** Ein Projekt eines Service referenziert von einem anderen Service nur dessen `.Contracts`. Der AppHost ist kein Service, er referenziert jede Api, um sie zu starten.
   - Prüffrage: Referenziert ein Projekt unter `src/Services` Domain, Application, Infrastructure, Api oder Worker eines anderen Service?
   - Test: `Rule2ContractsTests` liest die Projektreferenzen aller Projekte unter `src/Services`.
3. **Schichten zeigen nach innen.** Ein Projekt eines Service referenziert direkt nur, was in der Tabelle [Schichten eines Service](#schichten-eines-service) unter „darf referenzieren“ steht. Domain und Application kennen weder EF Core noch ASP.NET Core, auch nicht über eine andere Referenz.
   - Prüffrage: Referenziert ein Projekt eines Service direkt etwas, das nicht in seiner Zeile der Tabelle steht, oder hängen Domain oder Application direkt oder geerbt an EF Core oder ASP.NET Core?
   - Test: `Rule3LayerTests` vergleicht die direkten Projektreferenzen je Schicht mit der Tabelle, ein Projekt mit einer Schicht außerhalb der Tabelle ist auch ein Verstoß. Für Domain und Application liest er alle Abhängigkeiten aus `obj/project.assets.json`, auch die geerbten, also erst nach einem Restore.
4. **Integration nur über Ereignisse oder Abfragen.** Services tauschen Daten auf genau zwei Wegen aus: asynchron über Integration Events aus den `.Contracts` des Absenders über den Message Broker [NATS JetStream](decisions/broker.md), oder synchron per HTTP-Abfrage. Die Adresse des anderen Service steht nie im Service selbst, sie kommt von außen: lokal vom AppHost über die Service Discovery von Aspire, auf dem Server aus `deploy/compose.yml`. Geteilte Dateien und einen geteilten Cache gibt es nicht. Ein `.Contracts` hängt nur an `BuildingBlocks.Contracts`.
   - Prüffrage: Tauschen zwei Services Daten auf einem anderen Weg aus, oder steht eine Adresse fest im Service?
   - Test: `Rule4IntegrationTests` liest für jedes `.Contracts` alle Abhängigkeiten aus `obj/project.assets.json`, auch die geerbten: Dort darf nur `BuildingBlocks.Contracts` stehen. Im Code (`*.cs`) und in den `appsettings*.json` unter `src/Services` steht keine URL, keine IPv4-Adresse und kein `localhost`. Erlaubt sind ein Name der Service Discovery (`https+http://<ressource>`), `localhost` in der Design-Time-Factory des DbContext, die nur `dotnet ef` nutzt, und Kommentarzeilen. Geteilte Dateien oder ein geteilter Cache fallen nur im Review auf.
5. **Keine Projektionen fremder Daten.** Von Daten eines anderen Service speichert ein Service nur die ID und Werte, die er selbst daraus berechnet, zum Beispiel die Zahl fälliger Karten je Thema. Eine Kopie fremder Inhalte als Read-Model legt er nicht an. Braucht ein Screen Daten aus mehreren Services, holt der Client sie über das Gateway.
   - Prüffrage: Speichert ein Service Inhalte, die einem anderen Service gehören?
   - Test: `Rule5ProjectionTests` baut das Modell jedes DbContext über seine Design-Time-Factory, wie `dotnet ef`, ohne Datenbank. Kein Entity-Typ, kein Complex Type und kein Property-Typ darin stammt aus einem anderen Service, auch nicht als generisches Argument. Ein DbContext ohne Factory ist ein Verstoß, ebenso ein Projekt eines Service, das `Edvaniq.ArchitectureTests` nicht referenziert. Keine Domain referenziert ein `.Contracts`. Wer fremden Inhalt in eine eigene Spalte kopiert, etwa einen Text als `string`, fällt nur im Review auf.
6. **Zyklen per Abfrage.** Die Integration Events bilden keinen Kreis: Hört A auf Events von B, hört B weder direkt noch über andere Services auf Events von A. Braucht B trotzdem Daten von A, fragt B sie per HTTP ab. Die Typen für Anfrage und Antwort legt B selbst an, so entsteht keine Referenz auf A.
   - Prüffrage: Hat der Graph „Service referenziert `.Contracts` von“ einen Kreis?
   - Test: `Rule6CycleTests` sucht Kreise in den Projektreferenzen zwischen den Services und in der Tabelle unten.

Weitere Regeln:

- Clients kennen kein Backend-Projekt. `Client.Core` spricht nur mit dem Gateway, im Browser über den Web-Host. Die API-Clients entstehen per OpenAPI.
- BuildingBlocks enthalten nur technische Bausteine, keine Fachbegriffe.
- Paketversionen stehen nur in `Directory.Packages.props`.
- Neue Services entstehen nur aus der Vorlage in `templates/service/`, nie von Hand ([Einen neuen Service anlegen](service-template.md)).
- Jede Anfrage an einen Service braucht ein gültiges Token. Der Nutzer kommt nur aus dem Token (`sub`), nie aus der Anfrage ([Aufbau eines Service](service-internals.md#token-prüfung)).

Wer auf wessen Integration Events hört, nach Regel 6 ohne Kreis. Planning holt die Mastery per Abfrage bei LearningEngine, weil LearningEngine schon auf Planning hört.

| Consumer | konsumiert von |
|---|---|
| Planning | Identity, Content, Knowledge |
| Content | Identity |
| Knowledge | Identity, Content |
| Assessment | Identity, Content, Knowledge |
| Flashcards | Identity, Content, Knowledge, Assessment |
| LearningEngine | Identity, Planning, Knowledge, Assessment, Flashcards |
| Tutor | Identity, Content, Knowledge |
| Gamification | Identity, Planning, Assessment, Flashcards, LearningEngine |
| Analytics | Identity, Planning, Assessment, Flashcards, LearningEngine, Gamification |
| Notifications | Identity, Planning, Flashcards, Gamification |

## Gateway

Clients sprechen nie direkt mit einem Service, sondern immer mit dem Gateway, und das reicht die Anfrage weiter. Welcher Pfad zu welchem Service führt, steht nur in der Konfiguration, im Abschnitt `ReverseProxy` der `appsettings.json` des Gateways. Jeder Service hat dort einen eigenen Pfad, den das Gateway vor dem Weiterreichen abschneidet. `POST /planning/ping` kommt bei Planning also als `POST /ping` an.

Als Ziel steht dort ein Name der Service Discovery, etwa `https+http://planning-api`. Lokal löst Aspire ihn auf, auf dem Server die Variable `services__planning-api__http__0` beim Dienst `gateway` in `deploy/compose.yml`. Das Token reicht das Gateway unverändert durch, geprüft wird es im Service.

Kennt das Gateway einen Pfad nicht, antwortet es mit 404. Ist der Service nicht erreichbar, kommt 502. Bleibt er länger stumm, als `HttpRequest:ActivityTimeout` am Cluster erlaubt, kommt 504. Der Wert liegt unter den 10 Sekunden, die ein Aufrufer mit der Standard-Resilience je Versuch wartet, sonst gäbe der vorher auf. In allen drei Fällen steht im Body ein Problem nach RFC 9457 (`application/problem+json`), das den Grund nennt. Eine gescheiterte Anfrage wiederholt das Gateway nicht von selbst, weil ein `POST` sonst doppelt ankommen könnte. Ob das Gateway selbst läuft, sagt `GET /status` mit 204.

## Clients

```
Edvaniq.Web (Host) ──► Edvaniq.Web.Client (WASM) ──┐
                                                    ├──► Client.UI ──► Client.Core (API, Auth, State, Offline)
Edvaniq.App (MAUI Blazor Hybrid) ───────────────────┘         └──► Client.DesignSystem (Tokens, Basis-Komponenten)
```

Seiten und Features entstehen einmal in `Client.UI` und laufen dann im Browser und in der App.

Auch die Shell liegt dort: `ShellLayout` mit Kopfzeile, Navigation und dem Hinweis, wenn der Server nicht antwortet, dazu `Routes`. `Edvaniq.Web.Client` und die App binden nur `Routes` ein und registrieren `IBackendStatus` und `IPing` aus `Client.Core`. Die Seite `/systemstatus` schickt den Ping über Web-Host, Gateway und Planning in dessen Datenbank und zeigt, was dort gespeichert wurde. Ohne Sitzung startet `HttpPing` selbst eine Gast-Sitzung und versucht es einmal neu. Farben, Abstände und Grundstile stehen als CSS-Variablen in `Client.DesignSystem/wwwroot/edvaniq.css`. Ein Test prüft jedes Paar aus Text- und Hintergrundfarbe gegen WCAG AA, eine neue Farbe bekommt dort ihr Paar.

Eine unbekannte Adresse zeigt `NotFound`. Bei einem unerwarteten Fehler auf dem Server zeigt der Web-Host `ServerError`, auch aus `Client.UI`. Die Seite sagt kurz und ohne Details, dass etwas schiefgegangen ist, und nennt die Kennung der Anfrage. „Erneut versuchen“ holt dieselbe Seite noch einmal, ein zweiter Link führt zur Startseite. `ServerError` kommt fertig gerendert vom Server und braucht die Laufzeit im Browser nicht, weil die Seite auch bei einer langsamen Verbindung da sein soll. Aus demselben Grund sagt `/systemstatus`, bis die Laufzeit geladen ist, „Wird geladen …“, als Statusmeldung, die ein Screenreader vorliest.

Der Browser spricht nur mit dem Web-Host, nie mit dem Gateway. Ans Gateway reicht der Web-Host serverseitig nur die Routen aus dem Abschnitt `ReverseProxy` seiner `appsettings.json` weiter: `GET /api/status` und `POST /api/planning/ping`. Alles andere unter `/api` beantwortet er selbst mit 404, es erreicht das Gateway nie, auch der Token-Endpunkt von Identity nicht. Ein neues Feature bekommt dort eine eigene Route. Dafür braucht es eine Sitzung, einen verschlüsselten Cookie, den kein Skript lesen kann und den der Browser nur über HTTPS an diese Seite schickt. Ohne Sitzung antwortet der Web-Host mit 401. Den Cookie und einen `Authorization`-Header aus dem Browser gibt er nicht weiter, und Cookies aus dem Backend kommen nicht beim Browser an. Einen Login gibt es noch nicht: `POST /session` startet eine Gast-Sitzung, `DELETE /session` beendet sie, `GET /session` sagt, ob eine besteht. Nur `/api/status` geht ohne Sitzung durch, damit die Shell auch ohne Sitzung erfährt, ob das Gateway antwortet. Routen, die das technische Token tragen, zählt der Web-Host: höchstens `BackendToken:PerClientPerMinute` Aufrufe pro Minute je Client-Adresse und `BackendToken:TotalPerMinute` insgesamt, darüber antwortet er mit 429. So bekommt die Datenbank von Planning nur eine begrenzte Zahl Pings, egal von wie vielen Adressen sie kommen.

Eine Anfrage, die etwas ändert (`POST`, `PUT`, `PATCH` und `DELETE`), nimmt der Web-Host nur von den eigenen Seiten an. Schickt der Browser `Sec-Fetch-Site`, muss der Wert `same-origin` sein. Ältere Browser ohne diesen Header prüft der Web-Host am `Origin`, der die öffentliche Adresse des Web-Hosts sein muss. Sonst antwortet der Web-Host mit 403 und einem Problem. Weder der Endpunkt noch das Gateway sehen die Anfrage. Eine fremde Seite kann die Sitzung des Besuchers so nicht benutzen, auch wenn der Browser den Cookie mitschickt. Ein Aufrufer ohne Browser, etwa die App, schickt keinen der beiden Header und kommt durch, er hat keinen Cookie, den man ihm unterschieben könnte. Lesende Anfragen prüft der Web-Host nicht.

Jede Antwort des Web-Hosts trägt die Header aus der Tabelle, auch Fehlerantworten, Weiterleitungen und die Dateien der Shell. Außerhalb von Development kommt auf HTTPS-Antworten `Strict-Transport-Security` dazu. Gesetzt werden sie in `SecurityHeaders.cs`.

| Header | Wert |
|---|---|
| `Content-Security-Policy` | `frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'` |
| `X-Frame-Options` | `DENY` |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Cross-Origin-Opener-Policy` | `same-origin` |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=()` |

Damit lässt sich die Shell in keiner fremden Seite einbetten. Das Basis-Element und Formulare zeigen nur auf die eigene Seite, Plug-in-Inhalte sind gesperrt, und der Browser errät keinen Inhaltstyp. Kamera, Mikrofon und Standort sind für die Seite ausgeschaltet.

## Barrierefreiheits-Check

Grundlegende Fehler in der Shell sollen auffallen, bevor ein Nutzer darüber stolpert. `Edvaniq.Web.AccessibilityTests` startet dafür den echten Web-Host mit einem Stub als Gateway, lädt die Shell in Chromium und wartet, bis der Client läuft. Dann prüft axe-core jede Seite gegen WCAG 2.2 Level A und AA, darunter Kontrast, Beschriftungen, die Namen von Links und Schaltflächen, die Sprache des Dokuments und die Größe der Ziele. Gescannt werden `/`, `/systemstatus`, `/open-source` und die Seite für eine unbekannte Adresse, jede mit 375 px und mit 1280 px Breite. Dazu kommen die Shell mit geöffnetem Menü und `/systemstatus` bei ausgefallenem Server, weil nur dort die Warnfarben vorkommen.

Jede einzelne Verletzung macht den Test rot. Die Meldung nennt Regel, Auswirkung, betroffenes Element und die Seite von Deque, die erklärt, wie man es behebt. Die Tastatur sieht axe nicht, dafür gibt es eigene Tests: Der Skip-Link ist der erste Stopp und setzt den Fokus auf den Inhalt, das Menü öffnet und schließt sich mit Enter, Leertaste und Escape, und jeder Stopp zeigt einen sichtbaren Fokusrahmen. Ein weiterer Test lässt denselben Scan auf eine Seite mit absichtlichen Fehlern los (ein Bild ohne Alternativtext, ein Feld ohne Beschriftung, zu blasser Text) und verlangt, dass er sie meldet. So fällt auf, wenn der Check stumm wird.

Den Browser brauchst du lokal einmal. Nach dem ersten Build lädst du Chromium mit dem Skript, das Playwright neben die Tests legt:

```
dotnet build tests/Clients/Edvaniq.Web.AccessibilityTests
pwsh tests/Clients/Edvaniq.Web.AccessibilityTests/bin/Debug/net10.0/playwright.ps1 install --only-shell chromium
dotnet test --project tests/Clients/Edvaniq.Web.AccessibilityTests
```

Fehlt der Browser, bricht der Test mit genau diesem Befehl in der Meldung ab. In der CI lädt der Schritt „Install Chromium“ ihn vor den Tests. axe-core steht unter MPL-2.0 und läuft nur in diesen Tests, die Einzelfreigabe steht in [docs/licenses.md](licenses.md).

Der Check findet nur, was sich messen lässt. Ob ein Screenreader eine Seite sinnvoll vorliest, ob die Texte verständlich sind und ob die Reihenfolge stimmt, prüft ein Mensch. Das ist der manuelle Test mit Screenreader, für den Meilenstein M5 geplant ist. Was axe nicht entscheiden kann, etwa Text auf einem Farbverlauf, meldet es als „incomplete“, und das zählt im Check nicht als Verletzung.

## Nachverfolgung

Jede Anfrage hat eine Kennung, die W3C-Trace-ID. Der Server vergibt sie selbst: `traceparent` und seine Begleiter (`tracestate`, `baggage`) aus dem Internet verwirft der Reverse-Proxy vor dem Web-Host, so beginnt der Web-Host für jede Anfrage aus dem Browser eine eigene Trace. Der Web-Host selbst würde einen mitgeschickten `traceparent` übernehmen, lokal tut er das also. OpenTelemetry (ServiceDefaults) reicht sie im Header `traceparent` an Gateway und Services weiter, auch beim Token-Abruf bei Identity. Auch über den Broker geht die Kennung mit, die Outbox speichert dafür den `traceparent` mit jedem Ereignis. Außerhalb von Development schreibt jeder Prozess sein Log einzeilig auf die Konsole, mit den Scopes und damit mit der `TraceId`. Mit einer Kennung findet man so die Zeilen aller Prozesse, durch die die Anfrage ging, auch die der Services, die ihre Ereignisse behandelt haben.

Fehlerantworten nennen die Kennung im Feld `traceId` eines Problems nach RFC 9457, als 32 Hex-Zeichen. Über das Innere verraten sie sonst nichts: keine Exception, keinen Stacktrace, keine Adresse.
- Services aus der Vorlage rufen dafür als Erstes `app.UseProblems()` aus BuildingBlocks.Web auf. Eine unbehandelte Exception wird so zu einem nackten 500, ein 401 der Token-Prüfung und ein 404 ohne Body bekommen ebenfalls ein Problem.
- Das Gateway schreibt seine Probleme selbst.
- Der Web-Host macht aus den eigenen Fehlern unter `/api` und `/session` Probleme (`ApiProblems`). Probleme aus dem Backend reicht er unverändert durch, sie nennen dieselbe Kennung.
- Bei einer Seite zeigt der Web-Host im Fehlerfall `ServerError` mit derselben Kennung. Ein gescheiterter Ping auf `/systemstatus` nennt die Kennung seines Problems, `HttpPing` reicht sie dafür an die Seite durch.

## Tokens

Jeder Service prüft das Token selbst und muss dem Aussteller deshalb trauen, ohne ein Geheimnis mit ihm zu teilen. Identity signiert jedes Token mit seinem privaten Schlüssel (ES256), den nur Identity kennt. Den öffentlichen Teil holt sich ein Service über seine `Authority` direkt bei Identity, aus dem Discovery-Dokument unter `/.well-known/openid-configuration` und dem Key-Set, auf das es zeigt. Das Gateway leitet von Identity nur den Token-Endpunkt weiter.

Einen Login gibt es noch nicht. Bis dahin holt sich der Web-Host ein eigenes, technisches Token: Er schickt seine Client-ID `web` und sein Client-Secret an `POST /identity/connect/token` (OAuth 2.0 Client Credentials). Das Token gilt 5 Minuten, sein Subjekt `sub` ist der Client selbst. Der Web-Host hält es im Speicher und holt eine Minute vor Ablauf ein neues. Weil jeder ohne Login eine Gast-Sitzung starten kann, hängt er es nur an den einen Aufruf, den das Gerüst braucht: `POST /api/planning/ping` mit einer Sitzung. Welche Routen ein Token bekommen, sagt die Metadate `BackendToken` an der Route. Alle anderen Aufrufe gehen ohne Token weiter, und die Services weisen sie ab. Bekommt der Web-Host binnen 7 Sekunden kein Token, antwortet er mit 502 und reicht den Aufruf nicht weiter. Aufrufe, die gleichzeitig auf ein Token warten, teilen sich eine Anfrage an Identity. Der Browser sieht das Token nie.

Bis dahin steht in jedem Token derselbe Client statt eines Nutzers, für alle Sitzungen. Der Owner-Filter trennt Gäste also nicht voneinander, deshalb bekommt keine weitere Route das Token. Mit einem echten Login hängt der Web-Host das Token des Nutzers an statt seines eigenen. Issuer, Audience und der Weg zu den Schlüsseln bleiben, die Services merken davon nichts.

## Datenbank

Alle Services nutzen MySQL, einen Server mit einer Datenbank pro Service (`identitydb`, `planningdb` und so weiter). Ein Worker nutzt die Datenbank seines Service. Lokal startet Aspire MySQL als Container mit persistentem Volume und phpMyAdmin und legt die Datenbanken selbst an.

Den Connection String bekommt ein Service von Aspire als `ConnectionStrings:<service>db`, mit einem eigenen DB-Benutzer, der nur auf die eigene Datenbank darf (`AddServiceDatabase` im AppHost). Daten eines Nutzers (`IOwnedByUser`) sieht nur dieser Nutzer, weil der DbContext jedes Service von `ServiceDbContext` erbt. Der schränkt jede Abfrage auf den Nutzer aus dem Token ein ([Aufbau eines Service](service-internals.md#daten-je-nutzer)).

Das Schema ändert sich nur über EF-Core-Migrationen, und Migrieren ist ein eigener Schritt (`<Api>.dll migrate`), nie Teil des Starts ([Aufbau eines Service](service-internals.md#datenbank-und-migrationen)). Als Provider dient `MySql.EntityFrameworkCore` von Oracle, weil er EF Core 10 kann. `Pomelo.EntityFrameworkCore.MySql` gibt es bisher nur bis EF Core 9.

## Nachrichten

Services melden sich Ereignisse über NATS JetStream. Damit ein Ereignis weder verloren geht noch ohne seine Änderung hinausgeht, schreibt ein Service es mit der Änderung in die Tabelle `outbox_messages` seiner eigenen Datenbank, im selben `SaveChanges`. Ein Dispatcher im Prozess des Service schickt die Zeilen von dort an den Broker. Jeder Service hat beim Broker einen eigenen Stream, der nach ihm heißt, und seine Ereignisse liegen darunter. Wer ein Ereignis hören will, braucht nur dessen Typ aus den `.Contracts` des Absenders, Stream und Subject ergeben sich aus dem Namensraum.

Ein Consumer trägt jede Nachricht, die er behandelt, in seine Tabelle `inbox_messages` ein, im selben `SaveChanges` wie die Wirkung, und bestätigt sie erst danach beim Broker. Stellt der Broker eine Nachricht mehrmals zu, hat sie so nur eine Wirkung. Zugestellt wird mindestens einmal und ohne feste Reihenfolge. Beide Tabellen legt `ServiceDbContext` an, kein Service kann sie vergessen. Wie ein Service senden und empfangen kann, steht im [Aufbau eines Service](service-internals.md#nachrichten), warum es so gebaut ist, in den Entscheidungen zu [Outbox](decisions/outbox.md) und [Broker](decisions/broker.md).

Löscht Identity einen Nutzer, sendet es `UserDeleted`, und jeder Service aus der Vorlage entfernt alle Zeilen dieses Nutzers ([Nutzer löschen](service-internals.md#nutzer-löschen)).

Lokal startet der AppHost NATS und gibt Services aus der Vorlage die Adresse als `ConnectionStrings:nats`. Auf dem Server läuft noch kein NATS: Ein Service ohne diese Adresse sendet und empfängt nichts und verhält sich wie vorher, seine Ereignisse bleiben in der Outbox.

## Starten

Wie du das Backend und die Web-Shell lokal mit einem Befehl startest, steht in der [README](../README.md#lokal-starten).

## Neuen Service anlegen

Wie ein Service aus der Vorlage entsteht und auf den Server kommt, steht Schritt für Schritt in [Einen neuen Service anlegen](service-template.md). Was er mitbringt, steht im [Aufbau eines Service](service-internals.md).
