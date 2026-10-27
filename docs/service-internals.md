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
                                           <Name>DbContext.cs, ExampleItems.cs, Migrations/ (Initial, ExampleItems, Messaging, OutboxTraceParent, OwnerIdCollation)
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

Für jeden Handler läuft ein dauerhafter Consumer `<name>-<Handler>` im Stream des Absenders. Er behandelt eine Nachricht so: Er legt einen Eintrag für `inbox_messages` in den DbContext, ruft den Handler und speichert beides mit einem `SaveChanges`. Erst danach bestätigt er die Nachricht. Kennt die Inbox die `MessageId` für diesen Consumer schon, bestätigt er nur.

Ein Handler ändert deshalb nur den DbContext und ruft kein `SaveChanges`. Wirft er, wird nichts gespeichert, und der Broker stellt die Nachricht nach 5 Sekunden wieder zu. Hat der Absender noch nichts gesendet und deshalb noch keinen Stream, sagt der Consumer das einmal im Log und versucht es alle 5 Sekunden neu.

Ein Consumer arbeitet in der Trace der Anfrage, die das Ereignis ausgelöst hat. `IEventOutbox.Add` speichert dafür den `traceparent` mit dem Ereignis, der Dispatcher schickt ihn im Header `traceparent` mit, und der Consumer startet darunter eine Activity der Quelle `Edvaniq.Messaging`. Alles, was der Handler loggt, trägt so die `TraceId` dieser Anfrage, dazu `Consumer` und `MessageId`. Nach dem Speichern loggt der Consumer „Consumer <consumer> has handled message <MessageId>“.

Die Vorlage hört auf ihr eigenes `ExampleItemCreated`, damit der Weg von der Outbox bis in den Handler schon im Beispiel zu sehen ist. Ein echter Handler hört auf ein Event eines anderen Service. Eine Nachricht kann mehr als einmal und in anderer Reihenfolge ankommen, ein Handler verlässt sich auf beides nicht.

### Ohne Broker und in Tests

Die Adresse von NATS kommt nur von außen, als `ConnectionStrings:nats`. Lokal setzt sie der AppHost mit `WithMessaging(nats)`, auf dem Server läuft noch kein NATS. Fehlt die Adresse, starten weder Dispatcher noch Consumer, und der Service verhält sich, als gäbe es keine Nachrichten: Die Events bleiben in der Outbox, bis ein Broker da ist. Gesendete Outbox-Zeilen und Inbox-Einträge löscht der Service nach 14 Tagen (`Messaging:Retention`), ungesendete bleiben.

`ServiceFactory` tauscht den Broker mit `AddInMemoryTransport()` gegen `InMemoryTransport` aus, die Tests der Services brauchen also kein NATS. `MessagingTests` legt ein Item an und wartet, bis das Event gesendet ist und der Handler es in die Inbox eingetragen hat. Die Garantien des Bausteins selbst prüfen die Tests in `tests/BuildingBlocks`: Outbox, Inbox und ein Event zwischen zwei Services über NATS. `Edvaniq.BuildingBlocks.Messaging.UnitTests` laufen ohne Docker, die `IntegrationTests` brauchen Docker für MySQL und NATS.

## Nutzer löschen

Wird ein Konto gelöscht, müssen die Daten des Nutzers aus jedem Service verschwinden, nicht nur aus Identity. Identity sendet dafür `UserDeleted` aus `Identity.Contracts` mit der `UserId`, also dem `sub` seiner Tokens und damit der `OwnerId` seiner Zeilen. Bis sich ein Konto löschen lässt, kommt das Ereignis nur in den Tests vor.

Jeder Service aus der Vorlage hört mit dem `UserDeletedHandler` darauf, der `IUserDataRemover` aufruft. `AddInfrastructure` registriert beide:

```csharp
builder.Services.AddUserDataRemover<ServiceNameDbContext>();
builder.Services.AddIntegrationEventHandler<UserDeleted, UserDeletedHandler>();
```

Der Remover geht jede Entity mit `IOwnedByUser` im Modell durch und markiert alle Zeilen des Nutzers zum Löschen, auch solche, die ein eigener Query-Filter versteckt. Eine neue Entity mit `IOwnedByUser` ist damit ohne weiteren Code dabei. Zeilen ohne `OwnerId` gehen per Cascade Delete mit, deshalb muss ihre Beziehung Pflicht sein (`IsRequired`). Bei einer optionalen Beziehung scheitert das Löschen am Fremdschlüssel, und die Nachricht kommt immer wieder.

Gespeichert wird wie bei jedem Handler vom Consumer, zusammen mit dem Inbox-Eintrag. Ob ein Service die Daten entfernt hat, zeigen dieser Eintrag für `<name>-UserDeletedHandler` und die Logzeile „Consumer <name>-UserDeletedHandler has handled message <MessageId>“. Die Zeile trägt die `TraceId` der Anfrage, die den Nutzer gelöscht hat, aber nicht seine `UserId`. Mit dieser Kennung findet man die Bestätigung jedes Service ([Ereignis empfangen](#ereignis-empfangen)).

## Tests

`ServiceFactory` ist der Prüfstand: Er startet den echten Service, so wie er läuft, mit einer frischen MySQL-Datenbank, auf die alle Migrationen angewendet sind, und vertraut den Tokens aus `Edvaniq.Testing/TestTokens`. Den Broker tauscht er gegen einen im Prozess aus. Eine Testklasse holt ihn sich per `IClassFixture<ServiceFactory>`.

Die Datenbank ist echt. `MySqlTestServer` aus `Edvaniq.Testing` startet je Testlauf über Testcontainers einen MySQL-Container mit `mysql:9.7`, wie im AppHost, und `TestDatabase.cs` meldet ihn einmal fürs Projekt an. Jede `ServiceFactory` legt darin ihre eigene Datenbank mit einem eigenen Benutzer an, der nur auf sie darf. So stören sich parallele Testklassen nicht, und der Service meldet sich wie auf dem Server an, nie als root. Docker muss deshalb laufen, lokal wie in der CI.

Die Tests einer Klasse teilen sich eine Datenbank. Jeder Test erfindet seine Nutzer deshalb neu, etwa `alice-<guid>`. Die Vorlage bringt acht Testklassen mit:

- `TokenTests`: Abweisungen und der Nutzer nur aus dem Token
- `MigrationTests`: Die Migrationen passen zum Modell, laufen auf einer frischen DB und ein zweites Mal ohne Änderung. Jede `OwnerId` wird Byte für Byte verglichen.
- `IsolationTests`: Nutzer B sieht die Einträge von A weder in der Liste noch einzeln, auch dann nicht, wenn sich beide nur in der Groß-/Kleinschreibung der ID unterscheiden.
- `HealthTests`: mit und ohne Datenbank, auch außerhalb von Development
- `ProblemTests`: Fehler kommen als Problem mit der Trace-ID
- `DatabaseUserTests`: Der Benutzer einer Datenbank kann eine fremde weder lesen noch ändern noch auflisten und legt keine Datenbank an. Das `GRANT` stammt aus `ServiceDatabaseSql` im AppHost, die Tests kompilieren dieselbe Datei.
- `MessagingTests`: Ein neues Item schreibt sein Event in die Outbox, der Dispatcher sendet es, und der Handler bekommt es einmal.
- `UserDeletionTests`: Nach `UserDeleted` sind die Einträge des Nutzers weg und die anderer noch da, auch bei doppelter Zustellung und bei einer ID, die sich nur in der Groß-/Kleinschreibung unterscheidet. Inbox und Log belegen die Löschung.

Ob die Isolationstests wirklich etwas prüfen, zeigt die Gegenprobe. Ohne den Filter in `ServiceDbContext` werden alle rot, ohne die Collation nur die Tests mit den zwei Schreibweisen.

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

Genau das macht die CI bei jedem Lauf im Schritt „Check service template“ des Jobs „Build & test backend“. Zusätzlich prüft sie, dass alle sieben Projekte in der Solution stehen. Im nächsten Schritt baut sie das Image des Probe-Service und startet es (siehe [Container-Image](#container-image)).

## Stolperfallen

- Liefert Health 401, fehlen in `Program.cs` die Aufrufe `UseAuthentication()` und `UseAuthorization()` *nach* `MapDefaultEndpoints()`. Ohne sie setzt ASP.NET Core beide vor die ganze Pipeline, und außerhalb von Development verlangt dann auch Health auf 8081 ein Token. Docker sieht den Container nie healthy. Das deckt der Test `Health_OutsideDevelopment_AnswersWithoutToken` ab.
- Bekommt jedes gültige Token 403, fehlt `sub` im Code. Ohne `MapInboundClaims = false` benennt .NET den Claim in einen langen SOAP-Namen um. Die Einstellung steht in `AddTokenValidation`.
- Ein gerade abgelaufenes Token gilt noch eine Weile, weil .NET 5 Minuten Uhrabweichung duldet (`ClockSkew`).
- Wird ein Isolationstest rot, gibt es eine Entity ohne `IOwnedByUser`, einen DbContext, der nicht von `ServiceDbContext` erbt, oder eine Abfrage mit `IgnoreQueryFilters` ohne Not.
- Stell DB-Prüfung und Migration nicht auf async um. `OpenAsync` von `MySql.Data` ignoriert Timeout und Abbruch, wenn die Datenbank die Verbindung annimmt und dann schweigt, und hängt ohne Ende. Deshalb nutzt die Prüfung das synchrone `Open()` und der Migrationsschritt das synchrone `Migrate()`. Das deckt der Test `Health_WithSilentDatabase_ReportsUnhealthyInTime` ab.
- Wirft `IEventOutbox.Add` oder `AddIntegrationEventHandler` „is no integration event of a service“, steht der Typ nicht im Namensraum `Edvaniq.Services.<Name>.Contracts`. Der Namensraum bestimmt Stream und Subject.
- Ruft ein Handler `SaveChanges` auf, speichert er seine Änderung ohne den Eintrag in der Inbox. Kommt die Nachricht noch einmal, hat sie die Wirkung zweimal.