# Aufbau eines Service

Jeder Service aus der Vorlage bringt von Anfang an dasselbe mit: die Prüfung der Tokens, Health mit „lebendig“ und „bereit“, eine eigene Datenbank mit Migrationen, den Schutz der Daten je Nutzer, Outbox und Inbox für Integration Events, das Löschen der Daten eines Nutzers, einen Prüfstand für Integrationstests gegen eine echte Datenbank und ein Container-Image. Wie ein neuer Service entsteht, steht in [Einen neuen Service anlegen](service-template.md).

## Was entsteht

```
src/Services/<Name>/
  Edvaniq.Services.<Name>.Api              Program.cs: AddServiceDefaults, AddTokenValidation, AddInfrastructure, UseProblems, MapDefaultEndpoints, GET /me, Befehl migrate
                                           ExampleEndpoints.cs: Endpunkte des Beispiels
  Edvaniq.Services.<Name>.Application      IExampleItems.cs: was die Fachlogik vom Beispiel braucht
                                           ExampleItemCreatedHandler.cs: Beispiel für einen Handler
                                           UserDeletedHandler.cs: entfernt die Daten eines gelöschten Nutzers
  Edvaniq.Services.<Name>.Contracts        ExampleItemCreated.cs: Beispiel für ein Integration Event
  Edvaniq.Services.<Name>.Domain           ExampleItem.cs: Beispiel für Daten eines Nutzers
  Edvaniq.Services.<Name>.Infrastructure   InfrastructureExtensions.cs: DbContext, Beispiel, Nachrichten, Nutzer löschen und DB-Prüfung für Health
                                           <Name>DbContext.cs, ExampleItems.cs, Migrations/ (Initial, ExampleItems, Messaging, OutboxTraceParent, OwnerIdCollation, DeletedUsers)
tests/Services/<Name>/
  Edvaniq.Services.<Name>.UnitTests
  Edvaniq.Services.<Name>.IntegrationTests ServiceFactory.cs (Prüfstand), TestDatabase.cs
                                           HealthTests.cs, TokenTests.cs, MigrationTests.cs, IsolationTests.cs, ProblemTests.cs,
                                           DatabaseUserTests.cs,
                                           MessagingTests.cs, UserDeletionTests.cs
```

Alle sieben Projekte stehen danach in `Edvaniq.slnx`, die CI baut und testet sie also gleich mit. Die Ports in `launchSettings.json` wählt die Vorlage per Zufall, deshalb kann `Ports_AreUniqueAcrossProcesses` in `Edvaniq.ArchitectureTests` rot werden ([Wenn es nicht klappt](service-template.md#wenn-es-nicht-klappt)).

## Health

Ein Service meldet zwei Dinge. `/alive` sagt nur, dass der Prozess antwortet. `/health` sagt, dass er bereit ist, und prüft dafür zusätzlich die eigene Datenbank (`ConnectionStrings:<name>db`): verbinden und `SELECT 1`, beides mit höchstens 2 Sekunden.

| Lage | `/alive` | `/health` |
|---|---|---|
| DB erreichbar | 200 `Healthy` | 200 `Healthy` |
| DB gestoppt oder nicht erreichbar | 200 `Healthy` | 503 `Unhealthy` nach etwa 2 s |
| DB hängt (Container pausiert oder eingefroren) | 200 `Healthy` | 503 `Unhealthy` nach 2–4 s |
| kein Connection String | 200 `Healthy` | 503 `Unhealthy` |
| ein Consumer scheitert seit 5 Minuten an derselben Nachricht | 200 `Healthy` | 503 `Unhealthy` mit einer Zeile `messaging: …` |

Die Antwort von `/health` ist der Status. Nur ein Check mit dem Tag `details` hängt eine Zeile mit Namen und Beschreibung an, heute nur der Check `messaging` ([Wenn ein Ereignis dauerhaft scheitert](#wenn-ein-ereignis-dauerhaft-scheitert)). Fehlermeldungen und Exceptions anderer Checks stehen nie darin. Der Healthcheck in `deploy/compose.yml` gibt die Antwort aus, so behält Docker sie im Zustand des Containers.

Außerhalb von Development antworten beide nur auf dem internen Port 8081 ([Deploy](deploy.md#health)).

Einen Ausfall der Datenbank stellst du bei laufendem AppHost mit Docker nach:

```
docker ps --filter name=mysql --format "{{.Names}}"   # der MySQL-Container, nicht mysql-phpmyadmin
docker pause <container>      # DB hängt
docker unpause <container>
docker stop <container>       # DB weg
docker start <container>
```

## Token-Prüfung

Jede Anfrage braucht ein gültiges Token, ein JWT im Header `Authorization: Bearer …`. Fehlt es, ist die Signatur falsch, passen Issuer oder Audience nicht oder ist es abgelaufen, antwortet der Service mit 401. Hat das Token keinen Nutzer (`sub`), gibt es 403. Das gilt über eine Fallback-Policy für jeden Endpunkt ohne eigene Regel, also auch für jeden neuen. Nur `/health` und `/alive` brauchen kein Token.

Wer der Aufrufer ist, sagt allein das Token. `ICurrentUser.Id` aus `BuildingBlocks.Application` liest nur den Claim `sub` des geprüften Tokens, eine Nutzer-ID in Query, Body oder Header zählt nie. `GET /me` zeigt, wer der Aufrufer laut Token ist.

Was ein Service prüft, steht im Abschnitt `Authentication:Schemes:Bearer`, den `AddJwtBearer` selbst liest. Der Service kennt nur diesen Vertrag, nicht den Aussteller. Wer die Tokens ausstellt, kann sich also ändern, ohne dass sich am Service etwas ändert.

| Schlüssel | Wert | woher |
|---|---|---|
| `ValidAudiences` | `edvaniq-api`, für alle Services gleich, weil das Gateway ein Token an alle weiterreicht | `appsettings.json` |
| `ValidIssuer` | der Issuer, lokal `dotnet-user-jwts` | lokal `appsettings.Development.json`, sonst die Umgebung |
| `Authority` | Adresse von Identity, von der der Service die öffentlichen Schlüssel holt. Den Issuer von Identity nimmt er dabei zusätzlich an, lokal also neben `dotnet-user-jwts` | AppHost bzw. die Umgebung |
| `RequireHttpsMetadata` | `false`, weil der Service Identity im Netz der App über HTTP erreicht | AppHost bzw. die Umgebung |

Ohne Issuer oder Audience startet der Service nicht. Sonst würde .NET die fehlende Prüfung still überspringen und auch Tokens annehmen, die für eine andere App gedacht sind.

Die Tokens stellt Identity aus, wie das läuft, steht in der [Architektur](architecture.md#tokens). Unter dem AppHost bekommt der Service also echte Tokens über Web-Host und Gateway. Zum Testen ohne Web-Host holst du dir ein Dev-Token:

```
dotnet user-jwts create --project src/Services/<Name>/Edvaniq.Services.<Name>.Api --name <Nutzer> --audience edvaniq-api --output token
```

Dessen Schlüssel liegt in den User Secrets der Api und gilt nur in Development. In den Tests stellt `ServiceFactory` den Service auf die Tokens aus `Edvaniq.Testing/TestTokens` um, die mit einem Schlüssel nur für diesen Testlauf signiert sind.

## Fehlerantworten

`app.UseProblems()` steht in `Program.cs` als Erstes nach dem Befehl `migrate`. Danach antwortet jeder Fehler als Problem nach RFC 9457 mit der Trace-ID der Anfrage im Feld `traceId`: ein 401 der Token-Prüfung, ein 404 ohne Body und eine unbehandelte Exception, die zu einem nackten 500 ohne Meldung und Stacktrace wird. Dieselbe ID steht in jeder Logzeile der Anfrage, auch bei Gateway und Web-Host ([Nachverfolgung](architecture.md#nachverfolgung)). `ProblemTests` prüft das für 401 und 404.

## Datenbank und Migrationen

Jeder Service hat seine eigene Datenbank `<name>db` und einen eigenen Benutzer `<name>`, der nur auf sie darf. Lokal legt der AppHost beides an (`AddServiceDatabase`). Das Passwort erzeugt er einmal und legt es in seinen User Secrets ab.

Der Service bekommt `ConnectionStrings:<name>db` mit diesem Benutzer, nie mit root. Dass MySQL ihm jeden Zugriff außerhalb seiner Datenbank verweigert, belegt `DatabaseUserTests` (siehe [Tests](#tests)). Die Datenbank ist nur im Netz der App erreichbar.

DbContext und Health-Check verbinden sich über `MySqlConnections.For`, und zwar ohne TLS. Der Grund liegt im Treiber: MySql.Data hält den Zustand des TLS-Aufbaus in statischen `Dictionary`-Feldern ohne gemeinsame Sperre, und gleichzeitig geöffnete Verbindungen machen ihn kaputt.

Das Schema ändert sich nur per Migration. Dafür nutzt der Service EF Core mit `MySql.EntityFrameworkCore`, die Migrationen liegen in `Infrastructure/Migrations`. Nach der leeren Migration `Initial` enthält die Datenbank nur die Tabelle `__EFMigrationsHistory`.

Migrieren ist ein eigener Schritt, ein normaler Start migriert nie. `dotnet Edvaniq.Services.<Name>.Api.dll migrate` spielt alle fehlenden Migrationen ein und endet mit Exit-Code 0, bei einem Fehler mit 1. Ein zweiter Lauf findet nichts zu tun und endet ebenfalls mit 0.

Lokal übernimmt das die Ressource `<name>-migrate`, und die API startet erst, wenn sie fertig ist. Auf dem Server ist es der Dienst `<name>-migrate` im Profil `migrate`, der nach der Sicherung und vor dem Umschalten läuft ([Deploy](deploy.md#migrationen)).

Für eine neue Migration änderst du das Modell in `<Name>DbContext` und rufst `dotnet ef migrations add <Migration> --project src/Services/<Name>/Edvaniq.Services.<Name>.Infrastructure` auf. Das braucht keine Datenbank. Vergisst du die Migration, schlägt der Test `Migrations_MatchTheModel` fehl.

Zeitpunkte sind `DateTime` in UTC. MySQL speichert keine Zeitzone, deshalb gibt `ServiceDbContext` jeder `DateTime`-Spalte einen Konverter mit: Gelesene Werte gelten als UTC, eine API schreibt sie also mit „Z“ aus, und eine Ortszeit (`DateTimeKind.Local`) lehnt er beim Speichern ab. Eine neue Spalte braucht dafür keine eigene Zeile. `DateTimeOffset` und `TimeOnly` gehören nicht ins Modell, ein Tag ohne Uhrzeit ist ein `DateOnly`. Die aktuelle Zeit kommt immer aus `TimeProvider`, den Lerntag liefert `StudyDay` aus `Edvaniq.SharedKernel`. Beides prüft `TimeRuleTests`.

Halte Migrationen klein. MySQL rollt Änderungen am Schema nicht zurück, eine Migration, die mittendrin abbricht, bleibt also halb angewendet. Dann hilft nur die Sicherung von vor der Migration.

## Daten je Nutzer

Eine Entity, die einem Nutzer gehört, implementiert `IOwnedByUser` aus `BuildingBlocks.Domain` und hat damit eine `OwnerId`. Erbt der DbContext des Service von `ServiceDbContext` aus `BuildingBlocks.Infrastructure`, bekommt jede so markierte Entity den Query-Filter `Owner`: Jede Abfrage sieht nur die Zeilen des Nutzers aus dem Token. Nutzer B kann die Daten von A also weder lesen noch ändern noch löschen, auch wenn eine Abfrage den Besitzer vergisst. Dazu kommen `OwnerId` als Pflichtfeld mit höchstens 128 Zeichen und ein Index darauf.

`OwnerId` wird Byte für Byte verglichen (Collation `utf8mb4_0900_bin`), „Alice“ und „alice“ sind also zwei Nutzer. Mit der Standard-Collation von MySQL, die Groß-/Kleinschreibung und Akzente ignoriert, sähe „Abc“ die Zeilen von „abc“, und das Löschen von „Abc“ entfernte auch deren Daten. Der Service verlässt sich so nicht darauf, in welcher Schreibweise der Aussteller die `sub` vergibt. Identity muss dafür dieselbe Person immer mit derselben Schreibweise ausstellen.

Der MySQL-Provider ignoriert `UseCollation` an einer Spalte und lässt `COLLATE` auch bei `AlterColumn` weg. `ServiceDbContext` setzt deshalb die Annotation `MySQL:Collation`, und eine neue Tabelle bekommt die Collation aus dem Modell. Eine Spalte, die es schon gibt, bekommt sie nur über ein selbst geschriebenes `ALTER TABLE … MODIFY COLUMN` in der Migration, `dotnet ef migrations add` erzeugt dafür eine leere Migration. Vorbild ist `OwnerIdCollation` in Planning. `OwnerIdColumns_AreComparedByteForByte` in `MigrationTests` prüft gegen die echte Datenbank, dass keine `OwnerId` die Standard-Collation hat.

Eigene Konfiguration kommt in `ConfigureModel`. `OnModelCreating` ist versiegelt, damit kein Service den Filter versehentlich verliert.

Läuft eine Abfrage ohne Nutzer, etwa im Hintergrund, wirft sie einen Fehler, statt alle Zeilen zu zeigen. Code, der bewusst alle Nutzer sieht, schreibt `IgnoreQueryFilters([ServiceDbContext.OwnerFilter])`. Eine abgeleitete Entity in derselben Tabelle bekommt den Filter von ihrer Basis, weil EF ihn nur an der Wurzel annimmt.

Ein fremder Eintrag liefert 404, genau wie einer, den es nicht gibt, denn ein 403 würde verraten, dass er existiert. Beim Anlegen kommt `OwnerId` aus `ICurrentUser`. In Anfragen und Antworten taucht der Besitzer nie auf.

## Beispiel

`ExampleItem` zeigt an Daten eines Nutzers, wie die Schichten zusammenspielen: Domain (`ExampleItem`), Application (`IExampleItems`), Infrastructure (`ExampleItems`, Tabelle `ExampleItems`) und Api (`POST /examples`, `GET /examples`, `GET /examples/{id}`). Der Isolationstest der Vorlage läuft daran. Beim Anlegen schreibt `ExampleItems` auch ein Event in die Outbox ([Nachrichten](#nachrichten)).

Wie ein neuer Service das Beispiel durch seine erste echte Entity ersetzt, steht in Schritt 3 von [Einen neuen Service anlegen](service-template.md#schritte).

## Nachrichten

Jeder Service aus der Vorlage kann Ereignisse sicher senden und doppelt zugestellte Nachrichten nur einmal verarbeiten. Die Bausteine liegen in `BuildingBlocks.Messaging`, `AddInfrastructure` hängt sie mit `AddMessaging` an den DbContext des Service. Warum Outbox und NATS, steht in den Entscheidungen zu [Outbox](decisions/outbox.md) und [Broker](decisions/broker.md), der Überblick in der [Architektur](architecture.md#nachrichten).

### Ereignis senden

Ein Integration Event ist ein Record in den `.Contracts` des Service, der `IIntegrationEvent` implementiert. Sein Namensraum `Edvaniq.Services.<Name>.Contracts` legt fest, wohin es geht: in den Stream `<NAME>` mit dem Subject `<name>.<ereignis>`, dem Typnamen in Kleinbuchstaben. `ExampleItemCreated` von Planning geht also nach `planning.exampleitemcreated`. Ein Event trägt IDs und die Werte, die ein Hörer braucht, nicht den ganzen Inhalt (Regel 5). Ein Service sendet nur eigene Events, `IEventOutbox.Add` wirft bei einem fremden Typ. Jedes Event außer dem Beispiel muss im [Ereigniskatalog](spec/events.md) stehen, und seine Felder kommen in `EventContracts.txt` ([Der Vertrag im Code](spec/events.md#der-vertrag-im-code)).

Ein Use Case legt das Event über `IEventOutbox` aus `BuildingBlocks.Application` in die Outbox, bevor er speichert. `Add` sendet nichts, es legt eine Zeile für `outbox_messages` in den DbContext der Anfrage, und das `SaveChanges`, das die Änderung speichert, speichert sie mit. Scheitert es, fehlen Änderung und Event zusammen. So macht es `ExampleItems.AddAsync`:

```csharp
db.ExampleItems.Add(item);
outbox.Add(new ExampleItemCreated(item.Id, item.OwnerId));
await db.SaveChangesAsync(cancellationToken);
```

Gesendet wird in einem eigenen Schritt. Der Dispatcher läuft im Prozess der Api, holt jede Sekunde bis zu 50 offene Zeilen und schickt sie der Reihe nach an NATS, mit der `MessageId` der Zeile als `Nats-Msg-Id`. Danach markiert er sie als gesendet.

Nimmt NATS eine Nachricht nicht an, endet die Runde. Die Zeile merkt sich Fehler und Versuche in `LastError` und `Attempts`, und der Dispatcher probiert es nach einer wachsenden Pause wieder, höchstens alle 30 Sekunden. Kennt der Stream die `MessageId` schon, zählt die Nachricht als gesendet. Beim Start legt der Dispatcher den Stream des Service an oder gleicht ihn an: Nachrichten bleiben 7 Tage, höchstens 256 MB (`Messaging:StreamMaxAge`, `Messaging:StreamMaxBytes`).

### Ereignis empfangen

Ein Handler im Application-Projekt implementiert `IIntegrationEventHandler<TEvent>`. Das Event stammt aus den `.Contracts` eines anderen Service, und nur dieses Projekt darf der Service referenzieren (Regel 2). Die Beziehung kommt in die Tabelle in der [Architektur](architecture.md#service-regeln) und darf keinen Kreis erzeugen (Regel 6). Registriert wird der Handler in `AddInfrastructure`:

```csharp
builder.Services.AddIntegrationEventHandler<ExampleItemCreated, ExampleItemCreatedHandler>();
```

Für jeden Handler läuft ein dauerhafter Consumer `<name>-<Handler>` im Stream des Absenders. Er behandelt eine Nachricht so: Er legt einen Eintrag für `inbox_messages` in den DbContext, ruft den Handler und speichert beides mit einem `SaveChanges`. Erst danach bestätigt er die Nachricht. Vom ersten Nachsehen in der Inbox bis zum Speichern läuft das in einer Transaktion. Ein Handler arbeitet deshalb nur mit der eigenen Datenbank und ruft keinen anderen Service und nichts im Internet auf, sonst bleibt die Transaktion so lange offen. Kennt die Inbox die `MessageId` für diesen Consumer schon, bestätigt er nur.

Ein Handler ändert deshalb nur den DbContext und ruft kein `SaveChanges`. Wirft er, wird nichts gespeichert, und der Broker stellt die Nachricht nach 5 Sekunden wieder zu. Fehlt dem Handler noch etwas, das auf dem Weg ist, etwa ein Thema aus einem anderen Ereignis, wirft er `MessageNotReadyException`. Dann kommt die Nachricht genauso wieder, aber höchstens `Messaging:NotReadyDeliveries` Mal, voreingestellt 360, also etwa eine halbe Stunde. Danach bestätigt der Consumer sie ohne Wirkung und schreibt eine Warnung mit Consumer, `MessageId` und Zahl der Zustellungen ins Log. Jede andere Ausnahme kommt ohne Grenze wieder, damit etwa eine kurz fehlende Datenbank keine Nachricht kostet. Hat der Absender noch nichts gesendet und deshalb noch keinen Stream, sagt der Consumer das einmal im Log und versucht es alle 5 Sekunden neu.

Ein Consumer arbeitet in der Trace der Anfrage, die das Ereignis ausgelöst hat. `IEventOutbox.Add` speichert dafür den `traceparent` mit dem Ereignis, der Dispatcher schickt ihn im Header `traceparent` mit, und der Consumer startet darunter eine Activity der Quelle `Edvaniq.Messaging`. Alles, was der Handler loggt, trägt so die `TraceId` dieser Anfrage, dazu `Consumer` und `MessageId`. Nach dem Speichern loggt der Consumer „Consumer <consumer> has handled message <MessageId>“.

Die Vorlage hört auf ihr eigenes `ExampleItemCreated`, damit der Weg von der Outbox bis in den Handler schon im Beispiel zu sehen ist. Ein echter Handler hört auf ein Event eines anderen Service. Eine Nachricht kann mehr als einmal und in anderer Reihenfolge ankommen, ein Handler verlässt sich auf beides nicht.

### Reihenfolge

Der Broker hält die Reihenfolge nur innerhalb eines Stream, nicht zwischen Absendern. Ein Ereignis kann deshalb vor dem ankommen, auf das es sich bezieht. Jeder Empfänger kommt dann zum selben Ergebnis wie in der richtigen Reihenfolge, und zwar so:

| Empfänger | Ereignis vor seinem Bezug | Was passiert |
|---|---|---|
| Assessment | `ItemsGenerated` vor `TopicsChanged` mit dem Thema | Der Handler wirft `MessageNotReadyException`, die Nachricht kommt wieder, bis das Thema bekannt ist. Danach sind die Fragen da. |
| LearningEngine | `AnswerRecorded` vor `TopicsChanged` mit dem Thema | Der Lern-Nachweis wird gespeichert, aber erst gezeigt, wenn das Thema bekannt ist. Die Mastery entsteht immer aus allen Nachweisen, also wie in der richtigen Reihenfolge. Eine Info-Zeile nennt `AnswerId` und `TopicId`. |
| LearningEngine | `SessionCompleted` vor `TopicsChanged` | Die Einführung wird gespeichert und zählt, sobald das Thema bekannt ist. |
| Planning, Assessment, LearningEngine | ein älteres `TopicsChanged` nach einem neueren | Der Stand mit dem früheren `ChangedAt` ändert nichts (`TopicsSnapshot`). |

`OrderFlowTests` in `Edvaniq.EventFlowTests` schickt dieselben Ereignisse einmal in der richtigen und einmal in umgekehrter Reihenfolge über NATS und vergleicht Fragen und Mastery. Ein Ereignis eines gelöschten Nutzers, das nach `UserDeleted` ankommt, legt heute noch Daten an, das regelt [#944](https://github.com/NULLRADIX-DEV/Edvaniq/issues/944).

### Wenn ein Ereignis dauerhaft scheitert

Scheitert ein Handler an einer Nachricht länger als `Messaging:FailureAlarmAfter` (voreingestellt 5 Minuten), wird `/health` des Service `Unhealthy` und nennt Consumer, Ereignistyp und seit wann: `messaging: content-UserDeletedHandler cannot handle UserDeleted for 6 min`. `MessageId`, Inhalt und Exception stehen nicht darin, sie können Daten eines Nutzers enthalten und bleiben im Log, zu finden über Consumer und Zeit. Der Container gilt dann als `unhealthy`, und der Server alarmiert einmal und meldet „behoben“, sobald der Check wieder grün ist.

`MessageNotReadyException` zählt nicht. Sie ist der erwartete Fall, dass ein Ereignis des Absenders noch unterwegs ist, und eine solche Nachricht wird nach `Messaging:NotReadyDeliveries` Zustellungen ohnehin bestätigt. Würde sie zählen, gäbe es nach jedem Neustart des Absenders einen Fehlalarm. Auch ein Broker, der nicht erreichbar ist, zählt nicht: Dann gibt es keine Nachricht, an der ein Handler scheitert, und den Ausfall meldet der Container `nats` selbst.

Neu verarbeiten muss niemand von Hand. Der Broker stellt die Nachricht weiter alle 5 Sekunden zu. Ist der Fehler behoben und ausgerollt, gelingt die nächste Zustellung, der Check wird grün. Das gilt, solange der Stream die Nachricht noch hat, also bis zu 7 Tage (`Messaging:StreamMaxAge`). Der Zähler lebt im Prozess: Ein Neustart beginnt die Frist von vorn, die nächste gescheiterte Zustellung startet sie wieder.

Consumer laufen nur in den APIs, denn Worker haben keinen Healthcheck. `WorkerConsumerTests` in `Edvaniq.ArchitectureTests` prüft, dass kein Worker einen Handler registriert, außer er hat in `deploy/compose.yml` einen Healthcheck.

### Doppelte Zustellung

Dasselbe Ereignis kann auf zwei Wegen zweimal ankommen. Kommt dieselbe Nachricht noch einmal, also mit derselben `MessageId`, fängt sie die Inbox ab. Schickt der Absender dasselbe Ereignis noch einmal unter einer neuen `MessageId`, muss der Handler selbst erkennen, dass es nichts mehr zu tun gibt. Für jeden Consumer zeigt ein Integrationstest beide Fälle: Er stellt das Ereignis doppelt zu und prüft, dass sich der Zustand nach der ersten Zustellung nicht mehr ändert. Mit `Broker.DropsDuplicates = false` lässt der Broker im Test die doppelte `MessageId` durch.

Die Tabelle nennt diesen Test je Consumer als `Klasse.Methode` aus den Integrationstests des Service. `DuplicateDeliveryTests` in `Edvaniq.ArchitectureTests` vergleicht sie mit den Handlern im Code. Ein neuer Handler braucht deshalb eine Zeile, sonst wird der Test rot. Nur das Beispiel `ExampleItemCreated` der Vorlage braucht keine.

| Service | Ereignis | Test |
|---|---|---|
| Assessment | `DocumentDeleted` | `DocumentDeletionTests.DeletionDeliveredTwice_OrSentTwice_ChangesNothingMore` |
| Assessment | `ItemsGenerated` | `QuestionTests.ItemsDeliveredTwice_OrSentTwice_StoreEachQuestionOnce` |
| Assessment | `TopicsChanged` | `KnownTopicTests.StateDeliveredTwice_SentTwice_OrOlder_ChangesNothing` |
| Assessment | `UserDeleted` | `UserDeletionTests.DeletionDeliveredTwice_OrSentTwice_DoesNoHarm` |
| Content | `UserDeleted` | `UserDeletionTests.DeletionDeliveredTwice_OrSentTwice_DoesNoHarm` |
| Knowledge | `DocumentDeleted` | `DocumentDeletionTests.DeletionDeliveredTwice_OrSentTwice_ChangesNothingMore` |
| Knowledge | `DocumentAnalyzed` | `AnalysisTests.AnalysisDeliveredTwice_OrSentTwice_AddsNoTopicTwice`, `TopicsChangedTests.AnalysisDeliveredTwice_OrSentTwice_StoresNoSecondEvent` |
| Knowledge | `UserDeleted` | `UserDeletionTests.DeletionDeliveredTwice_OrSentTwice_DoesNoHarm` |
| LearningEngine | `AnswerRecorded` | `LearningRecordTests.AnswerDeliveredTwice_OrSentTwice_CountsOnce`, `MasteryTests.AnswerDeliveredTwice_OrSentTwice_MovesItOnce` |
| LearningEngine | `SessionCompleted` | `IntroductionTests.SessionDeliveredAgain_OrALaterSession_KeepsTheFirstIntroduction` |
| LearningEngine | `TopicsChanged` | `KnownTopicTests.StateDeliveredTwice_SentTwice_OrOlder_ChangesNothing` |
| LearningEngine | `UserDeleted` | `UserDeletionTests.DeletionDeliveredTwice_OrSentTwice_DoesNoHarm` |
| Planning | `TopicsChanged` | `KnownTopicTests.StateDeliveredTwice_SentTwice_OrOlder_ChangesNothing` |
| Planning | `UserDeleted` | `UserDeletionTests.DeletionDeliveredTwice_OrSentTwice_DoesNoHarm` |

### Wenn der Broker ausfällt

Ein Service arbeitet ohne Broker weiter, er sendet nur später. Jedes Event liegt mit seiner Änderung in der eigenen Datenbank, also wartet kein Speichern auf den Broker. Der Dispatcher versucht es mit wachsender Pause weiter, und der Client verbindet sich von selbst neu. Ein abgebrochener Consumer startet nach 5 Sekunden wieder. Was der Stream vor dem Ausfall schon hatte, behält der Broker über einen Neustart. Kommt er zurück, geht jedes offene Event raus und kommt genau einmal an, auch bei einem Empfänger, der während des Ausfalls gestartet ist.

`/health` eines Service hängt nicht am Broker. Fällt der Broker aus, bleibt jeder Service gesund und erreichbar, und gemeldet wird der Broker selbst über seinen eigenen Health-Check, einmal statt für jeden Service. Den Ablauf prüft `BrokerOutageTests` in `tests/BuildingBlocks` mit einem eigenen NATS, der gestoppt und wieder gestartet wird oder hängt.

### Ohne Broker und in Tests

Die Adresse von NATS kommt nur von außen, als `ConnectionStrings:nats`. Lokal setzt sie der AppHost mit `WithMessaging(nats)`, auf dem Server der Anker `x-messaging-environment` in `deploy/compose.yml`. Fehlt die Adresse, starten weder Dispatcher noch Consumer, und der Service verhält sich, als gäbe es keine Nachrichten: Die Events bleiben in der Outbox, bis ein Broker da ist. Gesendete Outbox-Zeilen und Inbox-Einträge löscht der Service nach 14 Tagen (`Messaging:Retention`), ungesendete bleiben.

`ServiceFactory` tauscht den Broker mit `AddInMemoryTransport()` gegen `InMemoryTransport` aus, die Tests der Services brauchen also kein NATS. `MessagingTests` legt ein Item an und wartet, bis das Event gesendet ist und der Handler es in die Inbox eingetragen hat. Die Garantien des Bausteins selbst prüfen die Tests in `tests/BuildingBlocks`: Outbox, Inbox und ein Event zwischen zwei Services über NATS. `Edvaniq.BuildingBlocks.Messaging.UnitTests` laufen ohne Docker, die `IntegrationTests` brauchen Docker für MySQL und NATS.

## Nutzer löschen

Wird ein Konto gelöscht, müssen die Daten des Nutzers aus jedem Service verschwinden, nicht nur aus Identity. Identity sendet dafür `UserDeleted` aus `Identity.Contracts` mit der `UserId`, also dem `sub` seiner Tokens und damit der `OwnerId` seiner Zeilen. Heute löscht der Betreiber ein Teamkonto mit `account delete` ([Teamkonto löschen](tokens.md#teamkonto-löschen)), Identity schickt das Ereignis im selben Speichern über seine Outbox.

Jeder Service aus der Vorlage hört mit dem `UserDeletedHandler` darauf, der `IUserDataRemover` aufruft. Der Handler ist zugleich die `IUserDeletion` des Service, also alles, was beim Löschen eines Nutzers passiert. Braucht ein Service mehr als den Remover, etwa Content für die Dateien, gehört das in dessen `DeleteAsync`. `AddInfrastructure` registriert beides:

```csharp
builder.Services.AddUserDeletion<ServiceNameDbContext, UserDeletedHandler>();
builder.Services.AddIntegrationEventHandler<UserDeleted, UserDeletedHandler>();
```

Der Remover geht jede Entity mit `IOwnedByUser` im Modell durch und markiert alle Zeilen des Nutzers zum Löschen, auch solche, die ein eigener Query-Filter versteckt. Eine neue Entity mit `IOwnedByUser` ist damit ohne weiteren Code dabei. Eine Entity ohne `IOwnedByUser` lässt `UserDataTests` in `Edvaniq.ArchitectureTests` nur mit einem Grund auf seiner Ausnahmeliste zu, etwa Summen ohne Nutzer. Den Weg durch alle Services prüft `UserDeletionFlowTests` in `Edvaniq.EventFlowTests`. Zeilen ohne `OwnerId` gehen per Cascade Delete mit, deshalb muss ihre Beziehung Pflicht sein (`IsRequired`). Bei einer optionalen Beziehung scheitert das Löschen am Fremdschlüssel, und die Nachricht kommt immer wieder.

Gespeichert wird wie bei jedem Handler vom Consumer, zusammen mit dem Inbox-Eintrag. Ob ein Service die Daten entfernt hat, zeigen dieser Eintrag für `<name>-UserDeletedHandler` und die Logzeile „Consumer <name>-UserDeletedHandler has handled message <MessageId>“. Die Zeile trägt die `TraceId` der Anfrage, die den Nutzer gelöscht hat, aber nicht seine `UserId`. Mit dieser Kennung findet man die Bestätigung jedes Service ([Ereignis empfangen](#ereignis-empfangen)).

### Späte Ereignisse

Zwischen den Streams der Services gibt es keine Reihenfolge. Ein `TopicsChanged` oder `AnswerRecorded` des Nutzers kann also nach `UserDeleted` ankommen und würde seine Daten wieder anlegen. Deshalb merkt sich jeder Service im selben Speichern wie die Löschung, dass der Nutzer gelöscht ist, in der Tabelle `deleted_users`. Der Consumer schaut dort vor jedem Handler nach: Gehört das Ereignis einem gelöschten Nutzer, bestätigt er es ohne Wirkung, mit Inbox-Eintrag und der Logzeile „Consumer <name> skipped <Ereignis> of a deleted user“.

Das gilt für jedes Ereignis, das `IUserEvent` aus `BuildingBlocks.Contracts` implementiert. `UserEventTests` in `Edvaniq.ArchitectureTests` verlangt das von jedem Ereignis mit einem Feld `UserId` oder `OwnerId`, ein neues fällt also von selbst darunter. Nur `UserDeleted` selbst ist keins. Heißt das Feld im Vertrag anders, implementiert das Ereignis die Eigenschaft explizit, so wie `ExampleItemCreated`.

Ein Handler, der kurz vor der Löschung nachgesehen hat, kann danach noch speichern. Dafür sucht `DeletedUserSweep` wenige Minuten nach jeder Löschung noch einmal nach Zeilen des Nutzers und führt, wenn er welche findet, die ganze `IUserDeletion` erneut aus, Dateien eingeschlossen.

Gespeichert wird nur ein SHA-256-Hash der `UserId` mit festem Präfix, nie die `UserId` selbst. Weil die `UserId` eine zufällige GUID ist, lässt sich der Hash nicht zurückrechnen, nur mit derselben Id aus einer anderen Quelle vergleichen. Nach 30 Tagen (`Messaging:DeletedUserRetention`) löscht die Aufräumrunde des Messaging den Eintrag. Das ist länger, als ein Ereignis unterwegs sein kann.

## Dokument löschen (Betreiber)

Ein Nutzer löscht sein Dokument mit `DELETE /documents/{id}`. Das Team kann ein Dokument auch ohne Token des Nutzers löschen, etwa Testdaten, mit einem Befehl im Prozess der Content-Api:

```bash
dotnet Edvaniq.Services.Content.Api.dll document delete <id>
```

Der Befehl startet keinen Webserver und hat keinen Weg über HTTP. Er sucht den Eigentümer des Dokuments und löscht dann in dessen Namen, genau wie der Endpunkt: Datei, Abschnitte und Fragenpools gehen mit, und `DocumentDeleted` landet im selben Speichern in der Outbox. Die laufende Content-Api schickt das Ereignis, Knowledge und Assessment entfernen, was aus dem Dokument entstanden ist. Er schreibt nur `deleted <id>` und endet mit 0. Gibt es das Dokument nicht, schreibt er `no document <id>`, schickt nichts und endet ebenfalls mit 0, ein zweiter Lauf ist also harmlos. Fehlt die ID oder ist sie keine GUID, endet er mit 2, bei einem anderen Fehler mit 1.

## KI pausieren (Betreiber)

Kostet die KI plötzlich zu viel oder antwortet sie Unsinn, hält das Team alle KI-Aufrufe sofort an, ohne Deploy, mit einem Befehl im Prozess der Content-Api:

```bash
dotnet Edvaniq.Services.Content.Api.dll ai pause
dotnet Edvaniq.Services.Content.Api.dll ai resume
dotnet Edvaniq.Services.Content.Api.dll ai status
```

Der Zustand steht in der Datenbank von Content, er gilt also sofort für jeden Prozess und übersteht Neustarts. Der Befehl schreibt `AI paused` oder `AI running` mit den Ausgaben des Monats und endet mit 0, bei falschen Argumenten mit 2, bei einem anderen Fehler mit 1. Jede echte Änderung steht als Logzeile im Log der Content-Api.

Pausiert erreicht kein KI-Aufruf von Content den Anbieter, auch `ai-check` nicht. Der Worker lässt jedes Dokument, das die KI bräuchte, mit „KI derzeit pausiert. Versuch es später noch einmal.“ scheitern, auch eines, das nach einem Ausfall gerade wartet. Ein Aufruf, der beim Pausieren schon läuft, darf zu Ende laufen. Nach `ai resume` startet der Nutzer das Dokument neu. Login, Lernziele, Listen und Upload rufen keine KI und laufen weiter.

### Monatsbudget

Ein Monatsbudget begrenzt, was die KI im Kalendermonat kosten darf. Gezählt wird in UTC, so wie der Anbieter abrechnet, und in seiner Währung, wie die Kosten im Kostenlog. Gesetzt wird es mit demselben Befehl:

```bash
dotnet Edvaniq.Services.Content.Api.dll ai budget 25.50
dotnet Edvaniq.Services.Content.Api.dll ai budget none
```

Ohne Budget gibt es kein Limit, der Worker schreibt dann beim Start eine Warnung ins Log. `ai status` zeigt, was im laufenden Monat schon ausgegeben ist, und das Budget. Beträge stehen nur in dieser Ausgabe, nie im Log. Die Summe beginnt jeden Monat bei 0, weil nur Versuche aus dem laufenden Monat zählen. Versuche ohne Preis zählen 0.

Ist das Budget aufgebraucht, lehnt die Content-Api Einfügen, Upload und Neustart mit 429 und „Das KI-Kontingent für diesen Monat ist aufgebraucht. Versuch es im nächsten Monat noch einmal.“ ab, ohne etwas zu speichern. Der Worker lässt Dokumente, die die KI noch bräuchten, mit derselben Meldung scheitern. Ein Aufruf, der gerade läuft, kann das Budget um seine eigenen Kosten überschreiten.

Der erste KI-Aufruf eines Monats, der 80 % des Budgets verbraucht findet, schreibt einmal eine Warnung ins Log, der erste, der es aufgebraucht findet, einmal einen Fehler. Beide nennen nur den Prozentsatz. Für den Betrieb bestimmt sind alle Zeilen, deren Text mit „AI budget:“ beginnt. Dieser Anfang, die Stufe und die Event-IDs bleiben, auch wenn sich der übrige Text ändert:

| Event-ID | Name | Stufe | Wann |
|---|---|---|---|
| 3031 | `AiBudgetWarning` | Warning | 80 % verbraucht, einmal je Monat |
| 3032 | `AiBudgetExhausted` | Error | Budget aufgebraucht, einmal je Monat |
| 3033 | `AiBudgetMissing` | Warning | kein Budget gesetzt, beim Start des Workers |
| 3034 | `AiBudgetUnknown` | Warning | Budget beim Start nicht lesbar |

Schalter und Budget sitzen vor dem `IChatClient` des Content-Workers (`AiGate`), den jeder KI-Aufruf passiert. Die Content-Api fragt mit derselben Summe (`IAiBudget`). Ruft später ein anderer Service eine KI, muss er beides beachten.

## Zeitgrenzen der Analyse

Kein Dokument bleibt für immer „in Analyse“. Die Grenzen des Content-Workers stehen im Abschnitt `Content:Analysis` der Konfiguration, ohne gültige Werte startet der Worker nicht:

| Schlüssel | Standard | Wofür |
|---|---|---|
| `AiTimeout` | 2 min | ein Aufruf der KI, danach gilt er als Ausfall |
| `TopicsTimeout` | 10 min | Warten auf die Themen von Knowledge, danach scheitert das Dokument |
| `Timeout` | 60 min | ein ganzer Lauf, gezählt ab Einfügen, Upload oder Neustart, muss größer als `TopicsTimeout` sein |
| `SweepInterval` | 1 min | wie oft der Worker nach zu langen Läufen sucht |

Ist ein Dokument nach `Timeout` noch „hochgeladen“ oder „in Analyse“, scheitert es mit „Die Analyse hat zu lange gedauert. Starte sie neu.“, etwa weil sein Worker immer wieder abstürzt. Der Nutzer startet es neu wie jedes gescheiterte Dokument. Ein Dokument, das nach einem Ausfall der KI absichtlich wartet, zählt erst wieder, wenn es an der Reihe ist. Beendet ein Worker das Dokument im selben Moment, gewinnt nur einer, es geht nichts doppelt hinaus. Jedes so beendete Dokument steht als Warnung im Log, dazu je Runde die Zahl. Häufen sich diese Zeilen, ist die Grenze zu knapp oder der Worker überlastet. War der Worker länger als `Timeout` aus, lässt seine erste Suche danach alle Dokumente scheitern, die schon vorher warteten; ein Neustart durch den Nutzer bringt jedes davon zurück.

## Tageskontingent an Seiten

Jeder Nutzer darf je Lerntag nur eine bestimmte Zahl an Seiten verarbeiten lassen, Standard 200 (`Content:PagesPerDay`, gilt für Api und Worker). Der Lerntag wechselt um 04:00 Uhr Europe/Berlin (`StudyDay`). Eine PDF zählt ihre Seiten. Eingefügter Text, TXT, Markdown und Word haben keine echten Seiten und zählen je angefangene 3.000 Zeichen eine Seite, mindestens eine. So umgeht eine riesige Textdatei das Kontingent nicht.

Gebucht wird im Worker, bevor der `DocumentSplitter` ein Dokument zerlegt. Erst dort kennt jedes Format seine Seiten, und vor der KI ist noch nichts ausgegeben. Passt das Dokument nicht in den Rest des Tages, scheitert es mit einer Meldung, die Seiten, freie Seiten und den Zeitpunkt der Rücksetzung nennt. Nach 04:00 Uhr geht ein Neustart durch. Jedes Dokument bucht genau einmal (`PageCharges`, eindeutig je Dokument): Ein Neustart bucht nichts nach. Löscht der Nutzer das Dokument, bleibt die Buchung, sonst würden Löschen und neu Hochladen das Kontingent zurücksetzen. Erst die Löschung des Nutzers entfernt sie.

Die Content-Api lehnt früher ab, wo sie kann, mit 429, der Meldung in `title` und dem Zeitpunkt der Rücksetzung in `resetsAt` (UTC). Bei eingefügtem Text kennt sie die Seiten schon, beim Upload noch nicht, dort lehnt sie nur ab, wenn nichts mehr frei ist. `GET /quota` zeigt dem Nutzer seinen eigenen Stand: `pagesLeftToday`, `pagesPerDay` und `resetsAt`. `generationsLeftToday` und `generationsPerDay` bleiben `null`, bis Generierungen gezählt werden. Api, Worker und `GET /quota` zählen mit derselben Klasse (`IPageQuota`).

## Tests

`ServiceFactory` ist der Prüfstand: Er startet den echten Service, so wie er läuft, mit einer frischen MySQL-Datenbank, auf die alle Migrationen angewendet sind, und vertraut den Tokens aus `Edvaniq.Testing/TestTokens`. Den Broker tauscht er gegen einen im Prozess aus. Eine Testklasse holt ihn sich per `IClassFixture<ServiceFactory>`.

Die Datenbank ist echt. `MySqlTestServer` aus `Edvaniq.Testing` startet je Testlauf über Testcontainers einen MySQL-Container mit `mysql:9.7`, wie im AppHost, und `TestDatabase.cs` meldet ihn einmal fürs Projekt an. Jede `ServiceFactory` legt darin ihre eigene Datenbank mit einem eigenen Benutzer an, der nur auf sie darf. So stören sich parallele Testklassen nicht, und der Service meldet sich wie auf dem Server an, nie als root. Docker muss deshalb laufen, lokal wie in der CI.

Die Tests einer Klasse teilen sich eine Datenbank. Jeder Test erfindet seine Nutzer deshalb neu, etwa `alice-<guid>`. Die Vorlage bringt acht Testklassen mit:

- `TokenTests`: Abweisungen und der Nutzer nur aus dem Token
- `MigrationTests`: Die Migrationen passen zum Modell, laufen auf einer frischen DB und ein zweites Mal ohne Änderung. Jede `OwnerId` wird Byte für Byte verglichen.
- `IsolationTests`: Nutzer B kann die Einträge von A über keinen Endpunkt lesen, ändern oder löschen, auch dann nicht, wenn sich beide nur in der Groß-/Kleinschreibung der ID unterscheiden ([Isolationsprüfung](#isolationsprüfung)).
- `HealthTests`: mit und ohne Datenbank, auch außerhalb von Development
- `ProblemTests`: Fehler kommen als Problem mit der Trace-ID
- `DatabaseUserTests`: Der Benutzer einer Datenbank kann eine fremde weder lesen noch ändern noch auflisten und legt keine Datenbank an. Das `GRANT` stammt aus `ServiceDatabaseSql` im AppHost, die Tests kompilieren dieselbe Datei.
- `MessagingTests`: Ein neues Item schreibt sein Event in die Outbox, der Dispatcher sendet es, und der Handler bekommt es einmal.
- `UserDeletionTests`: Nach `UserDeleted` sind die Einträge des Nutzers weg und die anderer noch da, auch bei doppelter Zustellung und bei einer ID, die sich nur in der Groß-/Kleinschreibung unterscheidet. Inbox und Log belegen die Löschung.

Ob die Isolationstests wirklich etwas prüfen, zeigt die Gegenprobe. Ohne den Filter in `ServiceDbContext` werden alle rot, ohne die Collation nur die Tests mit den zwei Schreibweisen.

### Isolationsprüfung

`IsolationCheck` aus `Edvaniq.Testing` prüft für alle Endpunkte eines Service, dass Nutzer B nichts von Nutzer A sieht. Jeder Service führt dafür in seinen `IsolationTests` eine Liste seiner Endpunkte:

```csharp
new IsolationCheck()
    .Exempt("GET", "/me", "Only the caller's own id from the token.")
    .List("GET", "/topics")
    .Item("GET", "/topics/{id:guid}")
    .Item("PATCH", "/topics/{id:guid}", new { name = "Von Bob" });
```

`Item` steht für eine Route mit der Id einer Ressource, `List` für eine, die mehrere liefert, und `Exempt` mit Begründung für eine ohne Daten anderer Nutzer, etwa eine, die nur eigene Daten anlegt. Die Begründungen stehen in der Testausgabe, damit ein Review sie sieht.

`RunAsync` legt über die Seed-Funktion des Service eine Ressource für A an. Danach fragt B jede `Item`-Route einmal mit der Id von A und einmal mit einer Id, die es nicht gibt, beide Male mit demselben Body. Beide Antworten müssen gleich sein, im Status und im Problem bis auf die Trace-ID, und keine darf Erfolg melden. Der Body muss gültig sein, sonst verdeckt ein 400 der Validierung, ob der Besitzer überhaupt geprüft wurde. Die Antwort einer `List`-Route an B darf weder die Id noch den Erkennungswert der Ressource von A enthalten. Zum Schluss liest A die Ressource und bekommt dasselbe wie vorher.

`AssertCovers` vergleicht die Liste mit den Endpunkten, die der laufende Service wirklich hat. Ein neuer Endpunkt lässt den Test also rot werden, bis er in der Liste steht. Ausgenommen sind nur Endpunkte, die jeder ohne Token erreicht, und solche, die nur das interne Token eines anderen Service annehmen. Die Endpunkte selbst ausprobieren kann die Prüfung nicht, weil sie weder gültige Bodies noch passende Daten kennt. Deshalb die Liste.

Dass die Prüfung ein Leck findet, zeigt `IsolationLeakTests` in Knowledge: Dort liest und ändert der Service Themen am Owner-Filter vorbei, und die Prüfung schlägt fehl.

## Container-Image

Jeder Service aus der Vorlage bekommt sein Image vom .NET SDK, ohne Dockerfile. Den Namen leitet `Directory.Build.props` aus dem Projektnamen ab: Aus `Edvaniq.Services.Sample.Api` wird `nullradix-dev/edvaniq/sample-api`. Bei laufendem Docker baust du das Image so, es landet im lokalen Docker und wird nirgends hochgeladen:

```
dotnet publish src/Services/<Name>/Edvaniq.Services.<Name>.Api -c Release --os linux --arch x64 -t:PublishContainer
```

Dass das ohne Handarbeit klappt und der Container unter den Grenzen des Servers läuft, beweist die CI bei jedem Lauf an dem frisch erzeugten Service `Sample`, im Schritt „Check service template image“ des Jobs „Build & test backend“. Der Schritt baut das Image wie oben und startet eine MySQL mit der Datenbank und dem eigenen Benutzer des Service. Dann führt er den Migrationsschritt aus, der mit Exit-Code 0 enden muss, und startet die Api.

Die Api bekommt die Grenzen für Speicher, Swap, CPU und Prozesse aus dem Anker `x-app` in `deploy/compose.yml` und dieselbe Härtung: schreibgeschütztes Dateisystem bis auf `/tmp`, keine Capabilities, kein Rechtegewinn und ein Netz ohne Zugang nach draußen. Der Schritt wartet höchstens zwei Minuten, bis Docker den Container als healthy meldet. Dafür fragt er `/health` auf Port 8081 so ab wie Docker auf dem Server. Scheitert etwas, stehen die Logs von Api und Datenbank im Log des Schritts.

Die Prüfung steckt in `deploy/check-template-image.sh`. Du kannst sie auch lokal für ein eigenes Image starten:

```
bash deploy/check-template-image.sh <name> nullradix-dev/edvaniq/<name>-api:<tag>
```

Braucht ein Service zum Start eine neue Einstellung, setzt `deploy/compose.yml` sie für den Server, und das Skript muss sie ebenfalls setzen. Sonst bleibt der Container in der CI hängen, obwohl er auf dem Server startet.

## Vorlage ändern

Die Dateien liegen unter `templates/service/`, die Einstellungen in `.template.config/template.json`. Drei Platzhalter ersetzt `dotnet new`: `ServiceName` durch den Namen, `servicename` durch den Namen in Kleinbuchstaben (etwa in `servicenamedb`) und die Ports `5999` und `7999` in `launchSettings.json` durch freie Ports.

Ob die Vorlage installiert ist, zeigt `dotnet new list edvaniq-service`, entfernt wird sie mit `dotnet new uninstall ./templates/service`. Nach jeder Änderung installierst du sie mit `--force` neu, denn `dotnet new` liest einen installierten Ordner nur beim Installieren ein und erzeugt sonst weiter den alten Stand. Zum Ausprobieren erzeugst du einen Probe-Service und räumst danach wieder auf:

```
dotnet new install ./templates/service --force
dotnet new edvaniq-service -n Sample
dotnet test --project tests/Services/Sample/Edvaniq.Services.Sample.IntegrationTests
rm -rf src/Services/Sample tests/Services/Sample
git restore Edvaniq.slnx
```

Genau das macht die CI bei jedem Lauf im Schritt „Check service template“ des Jobs „Architecture & template“. Zusätzlich prüft sie, dass alle sieben Projekte in der Solution stehen. Im nächsten Schritt baut sie das Image des Probe-Service und startet es (siehe [Container-Image](#container-image)).

## Stolperfallen

- Liefert Health 401, fehlen in `Program.cs` die Aufrufe `UseAuthentication()` und `UseAuthorization()` *nach* `MapDefaultEndpoints()`. Ohne sie setzt ASP.NET Core beide vor die ganze Pipeline, und außerhalb von Development verlangt dann auch Health auf 8081 ein Token. Docker sieht den Container nie healthy. Das deckt der Test `Health_OutsideDevelopment_AnswersWithoutToken` ab.
- Bekommt jedes gültige Token 403, fehlt `sub` im Code. Ohne `MapInboundClaims = false` benennt .NET den Claim in einen langen SOAP-Namen um. Die Einstellung steht in `AddTokenValidation`.
- Ein gerade abgelaufenes Token gilt noch eine Weile, weil .NET 5 Minuten Uhrabweichung duldet (`ClockSkew`).
- Wird ein Isolationstest rot, gibt es eine Entity ohne `IOwnedByUser`, einen DbContext, der nicht von `ServiceDbContext` erbt, oder eine Abfrage mit `IgnoreQueryFilters` ohne Not.
- Stell DB-Prüfung und Migration nicht auf async um. `OpenAsync` von `MySql.Data` ignoriert Timeout und Abbruch, wenn die Datenbank die Verbindung annimmt und dann schweigt, und hängt ohne Ende. Deshalb nutzt die Prüfung das synchrone `Open()` und der Migrationsschritt das synchrone `Migrate()`. Das deckt der Test `Health_WithSilentDatabase_ReportsUnhealthyInTime` ab.
- Wirft `IEventOutbox.Add` oder `AddIntegrationEventHandler` „is no integration event of a service“, steht der Typ nicht im Namensraum `Edvaniq.Services.<Name>.Contracts`. Der Namensraum bestimmt Stream und Subject.
- Ruft ein Handler `SaveChanges` auf, speichert er seine Änderung ohne den Eintrag in der Inbox. Kommt die Nachricht noch einmal, hat sie die Wirkung zweimal.