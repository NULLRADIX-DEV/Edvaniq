# Service-Vorlage

Jeder neue Service entsteht aus der Vorlage in `templates/service/`, damit alle gleich gebaut sind und keiner den Schutz der Daten vergisst. Die Vorlage legt mit `dotnet new` (Kurzname `edvaniq-service`) die fünf Projekte an und bringt alles mit, was ein Service von Anfang an braucht: die Prüfung der Tokens, Health mit „lebendig“ und „bereit“, eine eigene Datenbank mit Migrationen, den Schutz der Daten je Nutzer und einen Prüfstand für Integrationstests gegen eine echte Datenbank.

## Befehle

Alle Befehle laufen im Repo-Root.

| Was | Befehl |
|---|---|
| Vorlage installieren, einmalig und nach jeder Änderung an ihr | `dotnet new install ./templates/service --force` |
| Installierte Vorlage anzeigen | `dotnet new list edvaniq-service` |
| Vorschau, legt nichts an | `dotnet new edvaniq-service -n <Name> --dry-run` |
| Service anlegen | `dotnet new edvaniq-service -n <Name>` |
| Tests des Service, Docker muss laufen | `dotnet test --project tests/Services/<Name>/Edvaniq.Services.<Name>.IntegrationTests` |
| `dotnet ef` bereitstellen, einmalig je Klon | `dotnet tool restore` |
| Dev-Token für den lokalen Test | `dotnet user-jwts create --project src/Services/<Name>/Edvaniq.Services.<Name>.Api --name <Nutzer> --audience edvaniq-api --output token` |
| Migration anlegen, braucht keine DB | `dotnet ef migrations add <Migration> --project src/Services/<Name>/Edvaniq.Services.<Name>.Infrastructure` |
| Vorlage entfernen | `dotnet new uninstall ./templates/service` |

`<Name>` schreibst du in PascalCase, etwa `LearningEngine`. Daraus werden die Projekte `Edvaniq.Services.LearningEngine.*`, die Datenbank `learningenginedb` und der Name `learningengine-api` in AppHost und Compose.

## Was entsteht

```
src/Services/<Name>/
  Edvaniq.Services.<Name>.Api              Program.cs: AddServiceDefaults, AddTokenValidation, AddInfrastructure, MapDefaultEndpoints, GET /me, Befehl migrate
                                           ExampleEndpoints.cs: Endpunkte des Beispiels
  Edvaniq.Services.<Name>.Application      IExampleItems.cs: was die Fachlogik vom Beispiel braucht
  Edvaniq.Services.<Name>.Contracts
  Edvaniq.Services.<Name>.Domain           ExampleItem.cs: Beispiel für Daten eines Nutzers
  Edvaniq.Services.<Name>.Infrastructure   InfrastructureExtensions.cs: DbContext, Beispiel und DB-Prüfung für Health
                                           <Name>DbContext.cs, ExampleItems.cs, Migrations/ (Initial, ExampleItems)
tests/Services/<Name>/
  Edvaniq.Services.<Name>.UnitTests
  Edvaniq.Services.<Name>.IntegrationTests ServiceFactory.cs (Prüfstand), TestDatabase.cs
                                           HealthTests.cs, TokenTests.cs, MigrationTests.cs, IsolationTests.cs
```

Alle sieben Projekte stehen danach in `Edvaniq.slnx`, die CI baut und testet sie also gleich mit. Die Ports in `launchSettings.json` wählt die Vorlage per Zufall. Hat schon ein anderer Prozess denselben Port, wird der Test `Ports_AreUniqueAcrossProcesses` in `Edvaniq.ArchitectureTests` rot.

## Health

Ein Service meldet zwei Dinge. `/alive` sagt nur, dass der Prozess antwortet. `/health` sagt, dass er bereit ist, und prüft dafür zusätzlich die eigene Datenbank (`ConnectionStrings:<name>db`): verbinden und `SELECT 1`, beides mit höchstens 2 Sekunden.

| Lage | `/alive` | `/health` |
|---|---|---|
| DB erreichbar | 200 `Healthy` | 200 `Healthy` |
| DB gestoppt oder nicht erreichbar | 200 `Healthy` | 503 `Unhealthy` nach etwa 2 s |
| DB hängt (Container pausiert oder eingefroren) | 200 `Healthy` | 503 `Unhealthy` nach 2–4 s |
| kein Connection String | 200 `Healthy` | 503 `Unhealthy` |

Außerhalb von Development antworten beide nur auf dem internen Port 8081 ([Deploy](deploy.md#health)).

## Token-Prüfung

Jede Anfrage braucht ein gültiges Token, ein JWT im Header `Authorization: Bearer …`. Fehlt es, ist die Signatur falsch, passen Issuer oder Audience nicht oder ist es abgelaufen, antwortet der Service mit 401. Hat das Token keinen Nutzer (`sub`), gibt es 403. Das gilt über eine Fallback-Policy für jeden Endpunkt ohne eigene Regel, also auch für jeden neuen. Nur `/health` und `/alive` brauchen kein Token.

Wer der Aufrufer ist, sagt allein das Token. `ICurrentUser.Id` aus `BuildingBlocks.Application` liest nur den Claim `sub` des geprüften Tokens, eine Nutzer-ID in Query, Body oder Header zählt nie. `GET /me` zeigt, wer der Aufrufer laut Token ist.

Was ein Service prüft, steht im Abschnitt `Authentication:Schemes:Bearer`, den `AddJwtBearer` selbst liest. Der Service kennt nur diesen Vertrag, nicht den Aussteller. Wer die Tokens ausstellt, kann sich also ändern, ohne dass sich am Service etwas ändert.

| Schlüssel | Wert | woher |
|---|---|---|
| `ValidAudiences` | `edvaniq-api`, für alle Services gleich, weil das Gateway ein Token an alle weiterreicht | `appsettings.json` |
| `ValidIssuer` | der Issuer, lokal `dotnet-user-jwts` | lokal `appsettings.Development.json`, sonst die Umgebung |
| `Authority` | Adresse des Issuers, von der der Service die öffentlichen Schlüssel holt | die Umgebung |

Ohne Issuer oder Audience startet der Service nicht. Sonst würde .NET die fehlende Prüfung still überspringen und auch Tokens annehmen, die für eine andere App gedacht sind.

Zum lokalen Testen holst du dir ein Dev-Token mit `dotnet user-jwts` (siehe [Befehle](#befehle)). Dessen Schlüssel liegt in den User Secrets der Api und gilt nur in Development. In den Tests stellt `ServiceFactory` den Service auf die Tokens aus `Edvaniq.Testing/TestTokens` um, die mit einem Schlüssel nur für diesen Testlauf signiert sind.

## Datenbank und Migrationen

Jeder Service hat seine eigene Datenbank `<name>db` und einen eigenen Benutzer `<name>`, der nur auf sie darf. Lokal legt der AppHost beides an (`AddServiceDatabase`). Das Passwort erzeugt er einmal und legt es in seinen User Secrets ab. Der Service bekommt `ConnectionStrings:<name>db` mit diesem Benutzer, nie mit root. Die Datenbank ist nur im Netz der App erreichbar.

DbContext und Health-Check verbinden sich über `MySqlConnections.For`, und zwar ohne TLS. Der Grund liegt im Treiber: MySql.Data hält den Zustand des TLS-Aufbaus in statischen `Dictionary`-Feldern ohne gemeinsame Sperre, und gleichzeitig geöffnete Verbindungen machen ihn kaputt.

Das Schema ändert sich nur per Migration. Dafür nutzt der Service EF Core mit `MySql.EntityFrameworkCore`, die Migrationen liegen in `Infrastructure/Migrations`. Nach der leeren Migration `Initial` enthält die Datenbank nur die Tabelle `__EFMigrationsHistory`.

Migrieren ist ein eigener Schritt, ein normaler Start migriert nie. `dotnet Edvaniq.Services.<Name>.Api.dll migrate` spielt alle fehlenden Migrationen ein und endet mit Exit-Code 0, bei einem Fehler mit 1. Ein zweiter Lauf findet nichts zu tun und endet ebenfalls mit 0. Lokal übernimmt das die Ressource `<name>-migrate`, und die API startet erst, wenn sie fertig ist. Auf dem Server ist es der Dienst `<name>-migrate` im Profil `migrate`, der nach der Sicherung und vor dem Umschalten läuft ([Deploy](deploy.md#migrationen)).

Für eine neue Migration änderst du das Modell in `<Name>DbContext` und rufst `dotnet ef migrations add <Migration> --project …Infrastructure` auf (siehe [Befehle](#befehle)). Das braucht keine Datenbank. Vergisst du die Migration, schlägt der Test `Migrations_MatchTheModel` fehl.

Halte Migrationen klein. MySQL rollt Änderungen am Schema nicht zurück, eine Migration, die mittendrin abbricht, bleibt also halb angewendet. Dann hilft nur die Sicherung von vor der Migration.

## Daten je Nutzer

Eine Entity, die einem Nutzer gehört, implementiert `IOwnedByUser` aus `BuildingBlocks.Domain` und hat damit eine `OwnerId`. Erbt der DbContext des Service von `ServiceDbContext` aus `BuildingBlocks.Infrastructure`, bekommt jede so markierte Entity den Query-Filter `Owner`: Jede Abfrage sieht nur die Zeilen des Nutzers aus dem Token. Nutzer B kann die Daten von A also weder lesen noch ändern noch löschen, auch wenn eine Abfrage den Besitzer vergisst. Dazu kommen `OwnerId` als Pflichtfeld mit höchstens 128 Zeichen und ein Index darauf.

Eigene Konfiguration kommt in `ConfigureModel`. `OnModelCreating` ist versiegelt, damit kein Service den Filter versehentlich verliert.

Läuft eine Abfrage ohne Nutzer, etwa im Hintergrund, wirft sie einen Fehler, statt alle Zeilen zu zeigen. Code, der bewusst alle Nutzer sieht, wie der spätere Lösch-Consumer, schreibt `IgnoreQueryFilters([ServiceDbContext.OwnerFilter])`.

Ein fremder Eintrag liefert 404, genau wie einer, den es nicht gibt, denn ein 403 würde verraten, dass er existiert. Beim Anlegen kommt `OwnerId` aus `ICurrentUser`. In Anfragen und Antworten taucht der Besitzer nie auf.

## Beispiel

`ExampleItem` zeigt an Daten eines Nutzers, wie die Schichten zusammenspielen: Domain (`ExampleItem`), Application (`IExampleItems`), Infrastructure (`ExampleItems`, Tabelle `ExampleItems`) und Api (`POST /examples`, `GET /examples`, `GET /examples/{id}`). Der Isolationstest der Vorlage läuft daran.

Ein neuer Service ersetzt das Beispiel durch seine erste echte Entity:

1. Die eigene Entity nach demselben Muster anlegen, mit `IOwnedByUser`, wenn sie einem Nutzer gehört.
2. `ExampleItem`, `IExampleItems`, `ExampleItems`, `ExampleEndpoints` und die beiden Zeilen dafür (`AddScoped`, `MapExampleEndpoints`) löschen.
3. Eine Migration anlegen. Sie entfernt die Tabelle `ExampleItems` und legt die eigene an.
4. `IsolationTests` auf die eigenen Endpunkte umstellen. Der Test „Nutzer A sieht B nicht“ bleibt Pflicht.

## Tests

`ServiceFactory` ist der Prüfstand: Er startet den echten Service, so wie er läuft, mit einer frischen MySQL-Datenbank, auf die alle Migrationen angewendet sind, und vertraut den Tokens aus `Edvaniq.Testing/TestTokens`. Eine Testklasse holt ihn sich per `IClassFixture<ServiceFactory>`.

Die Datenbank ist echt. `MySqlTestServer` aus `Edvaniq.Testing` startet je Testlauf über Testcontainers einen MySQL-Container mit `mysql:9.7`, wie im AppHost, und `TestDatabase.cs` meldet ihn einmal fürs Projekt an. Jede `ServiceFactory` legt darin ihre eigene Datenbank mit einem eigenen Benutzer an, der nur auf sie darf. So stören sich parallele Testklassen nicht, und der Service meldet sich wie auf dem Server an, nie als root. Docker muss deshalb laufen, lokal wie in der CI.

Die Tests einer Klasse teilen sich eine Datenbank. Jeder Test erfindet seine Nutzer deshalb neu, etwa `alice-<guid>`. Die Vorlage bringt vier Testklassen mit:

- `TokenTests`: Abweisungen und der Nutzer nur aus dem Token
- `MigrationTests`: Die Migrationen passen zum Modell, laufen auf einer frischen DB und ein zweites Mal ohne Änderung.
- `IsolationTests`: Nutzer B sieht die Einträge von A weder in der Liste noch einzeln.
- `HealthTests`: mit und ohne Datenbank, auch außerhalb von Development

Ob die Isolationstests wirklich etwas prüfen, zeigt die Gegenprobe. Ohne den Filter in `ServiceDbContext` werden beide rot.

## Lokal mit echter Datenbank prüfen

1. Den Service in `AppHost.cs` eintragen (siehe [Danach von Hand](#danach-von-hand)) und den AppHost starten. Docker Desktop muss laufen.

   ```
   dotnet run --project src/Aspire/Edvaniq.AppHost
   ```

2. Den Port findest du in `src/Services/<Name>/Edvaniq.Services.<Name>.Api/Properties/launchSettings.json` (Profil `http`) und im Aspire-Dashboard.

   ```
   curl -i http://localhost:<port>/alive
   curl -i http://localhost:<port>/health
   ```

3. Einen Ausfall der Datenbank nachstellen:

   ```
   docker ps --filter name=mysql --format "{{.Names}}"   # Name des MySQL-Containers
   docker pause <container>      # DB hängt
   docker unpause <container>
   docker stop <container>       # DB weg
   docker start <container>
   ```

## Danach von Hand

1. In `Edvaniq.ArchitectureTests` die Referenzen auf die neuen Projekte ergänzen.
2. Im AppHost die Api als `ProjectReference` in `Edvaniq.AppHost.csproj` eintragen, in `AppHost.cs` diese Zeilen ergänzen und im Gateway `WithReference(<name>Api)` dazunehmen:

   ```csharp
   var <name>Db = mysql.AddServiceDatabase("<name>");
   var <name>Migrate = builder.AddMigration<Projects.Edvaniq_Services_<Name>_Api>("<name>-migrate", <name>Db);
   var <name>Api = AddService<Projects.Edvaniq_Services_<Name>_Api>("<name>-api", <name>Db)
       .WaitForCompletion(<name>Migrate);
   ```

3. In `deploy/compose.yml` einen Anker `x-<name>` anlegen, darauf den Dienst `<name>-api` und den Migrationsschritt `<name>-migrate`. Der Anker liest zusätzlich `<name>.env`, hängt im Netz `backend` und wartet auf `db`. Weil die Liste `env_file` die aus `x-app` ersetzt, steht `app.env` darin noch einmal. Die Datenbank hängt nur im Netz `backend`, und nur Prozesse mit eigener Datenbank kommen dazu. Vorbild ist Planning:

   ```yaml
   x-<name>: &<name>
     <<: *app
     env_file:
       - path: app.env
         required: true
       - path: <name>.env
         required: true
     networks: [default, backend]
     depends_on:
       db:
         condition: service_healthy

   services:
     <name>-api:
       <<: *<name>
       image: ghcr.io/nullradix-dev/edvaniq/<name>-api:sha-${APP_COMMIT:?}
     <name>-migrate:
       <<: *<name>
       image: ghcr.io/nullradix-dev/edvaniq/<name>-api:sha-${APP_COMMIT:?}
       command: ["migrate"]
       profiles: [migrate]
       restart: "no"
       healthcheck:
         disable: true
   ```

   Den Aussteller der Tokens (`Authentication__Schemes__Bearer__ValidIssuer`) setzt `x-app` für alle, ohne ihn startet ein Service aus der Vorlage nicht.

AppHost und Compose gehören zusammen, die CI prüft, dass beide Listen passen, auch bei den Migrationsschritten. Trag den Service aber erst ein, wenn seine Datenbank auf dem Server angelegt ist, sonst bricht der Deploy vor dem Umschalten ab.

## Gerüst ersetzen

Die übrigen Services stammen noch aus dem ersten Skelett: fünf leere Projekte, ohne Datenbank und ohne Tests. Planning ist so aus der Vorlage neu entstanden (#125):

1. Die sieben Projekte aus `Edvaniq.slnx` austragen (`dotnet sln Edvaniq.slnx remove …`) und beide Ordner ganz löschen, auch `bin/` und `obj/`. Sonst bricht `dotnet new` ab, weil es Dateien überschreiben müsste.
2. Den Service erzeugen wie oben. Die Pfade bleiben gleich, die Verweise anderer Services auf `.Contracts` und die Referenzen in `Edvaniq.ArchitectureTests` stimmen also weiter.
3. In `AppHost.cs` den Migrationsschritt ergänzen und die Api mit `WaitForCompletion` darauf warten lassen. `AddServiceDatabase` und `AddService` stehen schon da.
4. Die Datenbank des Service auf dem Server anlegen lassen, das übernimmt jemand mit Server-Zugang. Erst danach in `deploy/compose.yml` den Dienst `<name>-api` auf den eigenen Anker umstellen und den Migrationsschritt ergänzen, wie in [Danach von Hand](#danach-von-hand) Schritt 3. So ist Planning dazugekommen (#126).

## Vorlage ändern

Die Dateien liegen unter `templates/service/`, die Einstellungen in `.template.config/template.json`. Drei Platzhalter ersetzt `dotnet new`: `ServiceName` durch den Namen, `servicename` durch den Namen in Kleinbuchstaben (etwa in `servicenamedb`) und die Ports `5999` und `7999` in `launchSettings.json` durch freie Ports.

Zum Ausprobieren erzeugst du einen Probe-Service und räumst danach wieder auf:

```
dotnet new install ./templates/service --force
dotnet new edvaniq-service -n Sample
dotnet test --project tests/Services/Sample/Edvaniq.Services.Sample.IntegrationTests
rm -rf src/Services/Sample tests/Services/Sample
git restore Edvaniq.slnx
```

Genau das macht die CI bei jedem Lauf im Schritt „Check service template“ des Jobs „Build & test backend“. Zusätzlich prüft sie, dass alle sieben Projekte in der Solution stehen.

## Stolperfallen

- Heißt der neue Service `Edvaniq.Services.Edvaniq.*`, fehlte `-n`, und die Vorlage hat den Namen des aktuellen Ordners genommen.
- Außerhalb des Repos legt die Vorlage die Dateien zwar an, findet aber keine `Edvaniq.slnx` und endet mit Exit-Code 105. In einem Unterordner wie `src/` landet alles eine Ebene zu tief.
- Ohne `--force` erzeugt `dotnet new` weiter den alten Stand der Vorlage, weil es einen installierten Ordner nur beim Installieren einliest.
- Wird `Ports_AreUniqueAcrossProcesses` rot, hat die Vorlage einen Port gezogen, den schon ein anderer Prozess hat. Lösch den neuen Service und erzeug ihn neu, solange du noch nichts an ihm geändert hast. Sonst setz den Port in seiner `launchSettings.json` auf einen freien.
- Liefert Health 401, fehlen in `Program.cs` die Aufrufe `UseAuthentication()` und `UseAuthorization()` *nach* `MapDefaultEndpoints()`. Ohne sie setzt ASP.NET Core beide vor die ganze Pipeline, und außerhalb von Development verlangt dann auch Health auf 8081 ein Token. Docker sieht den Container nie healthy. Das deckt der Test `Health_OutsideDevelopment_AnswersWithoutToken` ab.
- Bekommt jedes gültige Token 403, fehlt `sub` im Code. Ohne `MapInboundClaims = false` benennt .NET den Claim in einen langen SOAP-Namen um. Die Einstellung steht in `AddTokenValidation`.
- Ein gerade abgelaufenes Token gilt noch eine Weile, weil .NET 5 Minuten Uhrabweichung duldet (`ClockSkew`).
- Scheitern die Integrationstests mit einem Docker-Fehler, läuft Docker Desktop nicht. Der Prüfstand braucht es für die Test-Datenbank.
- Wird ein Isolationstest rot, gibt es eine Entity ohne `IOwnedByUser`, einen DbContext, der nicht von `ServiceDbContext` erbt, oder eine Abfrage mit `IgnoreQueryFilters` ohne Not.
- Fehlt `dotnet ef`, hilft `dotnet tool restore`. Das Tool steht im Manifest `dotnet-tools.json`.
- Stell DB-Prüfung und Migration nicht auf async um. `OpenAsync` von `MySql.Data` ignoriert Timeout und Abbruch, wenn die Datenbank die Verbindung annimmt und dann schweigt, und hängt ohne Ende. Deshalb nutzt die Prüfung das synchrone `Open()` und der Migrationsschritt das synchrone `Migrate()`. Das deckt der Test `Health_WithSilentDatabase_ReportsUnhealthyInTime` ab.
