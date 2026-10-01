# Service-Vorlage

Neue Services entstehen nur aus der Vorlage in `templates/service/` (`dotnet new`, Kurzname `edvaniq-service`). Sie bringt die fünf Projekte, die Token-Prüfung, Health, das „lebendig“ und „bereit“ unterscheidet, eine eigene Datenbank mit Migrationen, den Schutz der Daten je Nutzer und einen Prüfstand für Integrationstests mit echter Datenbank mit.

## Befehle

Alle im Repo-Root ausführen.

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

`<Name>` in PascalCase, z. B. `LearningEngine`. Daraus werden `Edvaniq.Services.LearningEngine.*`, die Datenbank `learningenginedb` und der Name `learningengine-api` für AppHost und Compose.

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

- Alle sieben Projekte stehen danach in `Edvaniq.slnx`, die CI baut und testet sie also mit.
- Die Ports in `launchSettings.json` wählt die Vorlage je Service per Zufall. Hat schon ein anderer Prozess denselben Port, wird der Test `Ports_AreUniqueAcrossProcesses` in `Edvaniq.ArchitectureTests` rot.

## Health

| Endpunkt | Bedeutung | prüft |
|---|---|---|
| `/alive` | lebendig | nur, ob der Prozess antwortet |
| `/health` | bereit | zusätzlich die eigene Datenbank (`ConnectionStrings:<name>db`): verbinden und `SELECT 1`, je höchstens 2 s |

| Lage | `/alive` | `/health` |
|---|---|---|
| DB erreichbar | 200 `Healthy` | 200 `Healthy` |
| DB gestoppt oder nicht erreichbar | 200 `Healthy` | 503 `Unhealthy` nach etwa 2 s |
| DB hängt (Container pausiert oder eingefroren) | 200 `Healthy` | 503 `Unhealthy` nach 2–4 s |
| kein Connection String | 200 `Healthy` | 503 `Unhealthy` |

Außerhalb von Development antworten beide nur auf dem internen Port 8081 ([deploy.md](deploy.md#health)).

## Token-Prüfung

- **Jede Anfrage braucht ein gültiges Token** (JWT im Header `Authorization: Bearer …`). Ohne Token, mit falscher Signatur, falschem Issuer, falscher Audience oder abgelaufen antwortet der Service mit 401, ohne Nutzer (`sub`) mit 403. Das gilt per Fallback-Policy für jeden Endpunkt ohne eigene Regel, auch für neue. Nur `/health` und `/alive` brauchen kein Token.
- **Nutzer nur aus dem Token:** `ICurrentUser.Id` (aus `BuildingBlocks.Application`) liest nur den Claim `sub` des geprüften Tokens. Eine Nutzer-ID in Query, Body oder Header zählt nie. `GET /me` zeigt, wer der Aufrufer laut Token ist.
- **Vertrag** im Abschnitt `Authentication:Schemes:Bearer`, den `AddJwtBearer` selbst liest. Ein Service kennt nur ihn, nicht den Aussteller. Wer die Tokens ausstellt, kann sich ändern, ohne dass sich am Service etwas ändert.

  | Schlüssel | Wert | woher |
  |---|---|---|
  | `ValidAudiences` | `edvaniq-api`, für alle Services gleich, weil das Gateway ein Token an alle weiterreicht | `appsettings.json` |
  | `ValidIssuer` | der Issuer, lokal `dotnet-user-jwts` | lokal `appsettings.Development.json`, sonst die Umgebung |
  | `Authority` | Adresse des Issuers, von der der Service die öffentlichen Schlüssel holt | die Umgebung |

- **Ohne Issuer oder Audience startet der Service nicht.** Sonst würde .NET die fehlende Prüfung still überspringen und auch Tokens annehmen, die für eine andere App gedacht sind.
- **Lokal testen:** Ein Dev-Token kommt von `dotnet user-jwts` (siehe [Befehle](#befehle)). Dessen Schlüssel liegt in den User Secrets der Api und gilt nur in Development.
- **In den Tests** stellt `ServiceFactory` den Service auf die Tokens aus `Edvaniq.Testing/TestTokens` um, die mit einem Schlüssel nur für diesen Testlauf signiert sind.

## Datenbank und Migrationen

- **Eigene DB, eigener Benutzer:** Jeder Service hat die Datenbank `<name>db` und den Benutzer `<name>`, der nur auf sie darf. Lokal legt der AppHost beides an (`AddServiceDatabase`), das Passwort erzeugt er einmal und legt es in seinen User Secrets ab. Der Service bekommt `ConnectionStrings:<name>db` mit diesem Benutzer, nie mit root. Auf dem Server legt ein Werkzeug der Plattform beides an und schreibt den Connection String nach `<name>.env` ([Deploy](deploy.md#datenbank)).
- **Schema nur per Migration:** EF Core mit `MySql.EntityFrameworkCore`, die Migrationen liegen in `Infrastructure/Migrations`. Nach der leeren Migration `Initial` enthält die DB nur die Tabelle `__EFMigrationsHistory`.
- **Migrieren ist ein eigener Schritt:** `dotnet Edvaniq.Services.<Name>.Api.dll migrate` spielt alle fehlenden Migrationen ein und endet mit Exit-Code 0, bei einem Fehler mit 1. Ein zweiter Lauf findet nichts zu tun und endet auch mit 0. Ein normaler Start migriert nie. Lokal übernimmt das die Ressource `<name>-migrate`, die API startet erst, wenn sie fertig ist. Auf dem Server ist es der Dienst `<name>-migrate` im Profil `migrate`. Er läuft nach der Sicherung und vor dem Umschalten ([Deploy](deploy.md#migrationen)).
- **Neue Migration:** Modell in `<Name>DbContext` ändern, dann `dotnet ef migrations add <Migration> --project …Infrastructure` (siehe [Befehle](#befehle)). Das braucht keine Datenbank. Der Test `Migrations_MatchTheModel` schlägt fehl, wenn das Modell ohne Migration geändert wurde.
- **Grenze:** MySQL rollt DDL nicht zurück. Bricht eine Migration mittendrin ab, bleibt sie halb angewendet, und nur die Sicherung vor der Migration hilft. Deshalb Migrationen klein halten.

## Daten je Nutzer

- **Markieren:** Eine Entity, die einem Nutzer gehört, implementiert `IOwnedByUser` (`BuildingBlocks.Domain`) mit `OwnerId`.
- **Schutz:** Der DbContext des Service erbt von `ServiceDbContext` (`BuildingBlocks.Infrastructure`). Der gibt jeder markierten Entity den Query-Filter `Owner`: Jede Abfrage sieht nur die Zeilen des Nutzers aus dem Token. Nutzer B kann die Daten von A also weder lesen noch ändern noch löschen, auch wenn eine Abfrage den Besitzer vergisst. Dazu kommen `OwnerId` als Pflichtfeld mit höchstens 128 Zeichen und ein Index darauf.
- **Eigene Konfiguration** kommt in `ConfigureModel`. `OnModelCreating` ist versiegelt, damit kein Service den Filter verliert.
- **Ohne Nutzer**, etwa im Hintergrund, wirft eine Abfrage auf markierte Daten einen Fehler, statt alles zu zeigen. Code, der bewusst alle Nutzer sieht, wie der spätere Lösch-Consumer, schreibt `IgnoreQueryFilters([ServiceDbContext.OwnerFilter])`.
- **Fremd ist wie nicht vorhanden:** Ein fremder Eintrag liefert 404, wie einer, den es nicht gibt. Ein 403 würde verraten, dass er existiert.
- **Besitzer setzen:** Beim Anlegen kommt `OwnerId` aus `ICurrentUser`. In Anfragen und Antworten taucht der Besitzer nie auf.

## Beispiel

`ExampleItem` zeigt das Zusammenspiel der Schichten an Daten eines Nutzers: Domain (`ExampleItem`), Application (`IExampleItems`), Infrastructure (`ExampleItems`, Tabelle `ExampleItems`) und Api (`POST /examples`, `GET /examples`, `GET /examples/{id}`). Der Isolationstest der Vorlage läuft daran.

Ein neuer Service ersetzt das Beispiel durch seine erste echte Entity:

1. Die eigene Entity nach demselben Muster anlegen, mit `IOwnedByUser`, wenn sie einem Nutzer gehört.
2. `ExampleItem`, `IExampleItems`, `ExampleItems`, `ExampleEndpoints` und die beiden Zeilen dafür (`AddScoped`, `MapExampleEndpoints`) löschen.
3. Eine Migration anlegen. Sie entfernt die Tabelle `ExampleItems` und legt die eigene an.
4. `IsolationTests` auf die eigenen Endpunkte umstellen. Der Test „Nutzer A sieht B nicht“ bleibt Pflicht.

## Tests (Prüfstand)

- **`ServiceFactory`** startet den echten Service, wie er läuft. Er bekommt eine frische MySQL-Datenbank, auf die alle Migrationen angewendet sind, und vertraut den Tokens aus `Edvaniq.Testing/TestTokens`. Eine Testklasse holt sich ihn per `IClassFixture<ServiceFactory>`.
- **Echte Datenbank:** `MySqlTestServer` aus `Edvaniq.Testing` startet je Testlauf einen MySQL-Container (`mysql:9.7`, wie der AppHost) über Testcontainers. `TestDatabase.cs` meldet ihn einmal fürs Projekt an. Jede `ServiceFactory` legt darin ihre eigene Datenbank an, so stören sich parallel laufende Testklassen nicht. Docker muss laufen, lokal wie in der CI.
- **Nutzer je Test:** Die Tests einer Klasse teilen sich eine Datenbank. Deshalb erfindet jeder Test seine Nutzer neu, z. B. `alice-<guid>`.
- **Beispieltests:**
  - `TokenTests`: Abweisungen und der Nutzer nur aus dem Token
  - `MigrationTests`: Migrationen passen zum Modell, laufen auf einer frischen DB und ein zweites Mal ohne Änderung
  - `IsolationTests`: Nutzer B sieht die Einträge von A weder in der Liste noch einzeln
  - `HealthTests`: mit und ohne Datenbank, auch außerhalb von Development
- **Gegenprobe:** Ohne den Filter in `ServiceDbContext` werden beide Isolationstests rot.

## Lokal mit echter Datenbank prüfen

1. Den Service in `AppHost.cs` eintragen (siehe unten) und den AppHost starten. Docker Desktop muss laufen.

   ```
   dotnet run --project src/Aspire/Edvaniq.AppHost
   ```

2. Der Port steht in `src/Services/<Name>/Edvaniq.Services.<Name>.Api/Properties/launchSettings.json` (Profil `http`) und im Aspire-Dashboard.

   ```
   curl -i http://localhost:<port>/alive
   curl -i http://localhost:<port>/health
   ```

3. DB-Ausfall nachstellen:

   ```
   docker ps --filter name=mysql --format "{{.Names}}"   # Name des MySQL-Containers
   docker pause <container>      # DB hängt
   docker unpause <container>
   docker stop <container>       # DB weg
   docker start <container>
   ```

## Danach von Hand

1. `Edvaniq.ArchitectureTests`: Referenzen auf die neuen Projekte.
2. AppHost: die Api als `ProjectReference` in `Edvaniq.AppHost.csproj`, in `AppHost.cs` diese Zeilen und `WithReference(<name>Api)` im Gateway:

   ```csharp
   var <name>Db = mysql.AddServiceDatabase("<name>");
   var <name>Migrate = builder.AddMigration<Projects.Edvaniq_Services_<Name>_Api>("<name>-migrate", <name>Db);
   var <name>Api = AddService<Projects.Edvaniq_Services_<Name>_Api>("<name>-api", <name>Db)
       .WaitForCompletion(<name>Migrate);
   ```

3. `deploy/compose.yml`: ein Anker `x-<name>` mit `<name>.env` und dem Warten auf `db`, darauf der Dienst `<name>-api` und der Migrationsschritt `<name>-migrate`. Vorbild ist Planning:

   ```yaml
   x-<name>: &<name>
     <<: *app
     env_file:              # ersetzt die Liste aus x-app, deshalb steht app.env noch einmal da
       - path: app.env
         required: true
       - path: <name>.env
         required: true
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

   Den Aussteller der Tokens (`Authentication__Schemes__Bearer__ValidIssuer`) setzt `x-app` für alle, ohne ihn startet ein Service aus der Vorlage nicht. AppHost und Compose gehören zusammen, die CI prüft beide Listen, auch die Migrationsschritte. Erst eintragen, wenn die Datenbank des Service auf dem Server angelegt ist: Ohne `<name>.env` bricht der Deploy vor dem Umschalten ab.

## Gerüst ersetzen

Die übrigen Services stammen noch aus dem ersten Skelett: fünf leere Projekte, ohne Datenbank und Tests. Planning ist in #125 so aus der Vorlage neu entstanden:

1. Die sieben Projekte aus `Edvaniq.slnx` austragen (`dotnet sln Edvaniq.slnx remove …`) und beide Ordner ganz löschen, auch `bin/` und `obj/`. Sonst bricht `dotnet new` ab, weil es Dateien überschreiben müsste.
2. Erzeugen wie oben. Die Pfade bleiben gleich, die Verweise anderer Services auf `.Contracts` und die Referenzen in `Edvaniq.ArchitectureTests` stimmen also weiter.
3. In `AppHost.cs` den Migrationsschritt ergänzen und die Api mit `WaitForCompletion` darauf warten lassen. `AddServiceDatabase` und `AddService` stehen schon da.
4. Auf dem Server die Datenbank des Service anlegen (`EdvaniqDoc/Betrieb.md`). Erst danach in `deploy/compose.yml` den Dienst `<name>-api` auf den eigenen Anker umstellen und den Migrationsschritt ergänzen, wie in „Danach von Hand“ Schritt 3. Planning ist in #126 so dazugekommen.

## Vorlage ändern

- Die Dateien liegen unter `templates/service/`, die Einstellungen in `.template.config/template.json`.
- **Platzhalter:**
  - `ServiceName` wird zum Namen.
  - `servicename` wird zum Namen in Kleinbuchstaben, z. B. in `servicenamedb`.
  - `5999` und `7999` in `launchSettings.json` werden zu freien Ports.
- **Ausprobieren**, danach wieder aufräumen:

  ```
  dotnet new install ./templates/service --force
  dotnet new edvaniq-service -n Sample
  dotnet test --project tests/Services/Sample/Edvaniq.Services.Sample.IntegrationTests
  rm -rf src/Services/Sample tests/Services/Sample
  git restore Edvaniq.slnx
  ```

- Genau das macht die CI bei jedem Lauf im Schritt „Check service template“ des Jobs „Build & test backend“. Zusätzlich prüft sie, dass alle sieben Projekte in der Solution stehen.

## Stolperfallen

- **`-n` vergessen:** Dann heißt der Service wie der aktuelle Ordner, also `Edvaniq.Services.Edvaniq.*`.
- **Nicht im Repo-Root:**
  - Außerhalb des Repos legt die Vorlage die Dateien an, findet aber keine `Edvaniq.slnx` und endet mit Exit-Code 105.
  - In einem Unterordner wie `src/` landet alles eine Ebene zu tief.
- **Ohne `--force`** erzeugt `dotnet new` weiter den alten Stand der Vorlage. Ein installierter Ordner wird nur beim Installieren eingelesen.
- **`Ports_AreUniqueAcrossProcesses` rot:** Die Vorlage hat einen Port gezogen, den schon ein anderer Prozess hat, und im AppHost wollen beide auf ihm lauschen. Den neuen Service löschen und neu erzeugen, solange an ihm noch nichts geändert ist, sonst den Port in seiner `launchSettings.json` auf einen freien setzen.
- **Health liefert 401:** In `Program.cs` müssen `UseAuthentication()` und `UseAuthorization()` von Hand *nach* `MapDefaultEndpoints()` stehen. Fehlen die Aufrufe, setzt ASP.NET Core beide vor die ganze Pipeline, und außerhalb von Development verlangt auch die Health-Middleware auf 8081 ein Token. Docker sieht den Container dann nie healthy. Der Test `Health_OutsideDevelopment_AnswersWithoutToken` deckt das ab.
- **`sub` fehlt im Code:** Ohne `MapInboundClaims = false` benennt .NET `sub` in einen langen SOAP-Claim-Namen um, und jedes gültige Token bekommt 403. Steht in `AddTokenValidation`.
- **Abgelaufenes Token gilt noch:** .NET duldet 5 Minuten Uhrabweichung (`ClockSkew`).
- **Integrationstests scheitern mit einem Docker-Fehler:** Docker Desktop läuft nicht. Der Prüfstand braucht es für die Test-Datenbank.
- **Isolationstest rot:** Eine Entity ohne `IOwnedByUser`, ein DbContext, der nicht von `ServiceDbContext` erbt, oder eine Abfrage mit `IgnoreQueryFilters` ohne Not.
- **`dotnet ef` fehlt:** Das Tool steht im Manifest `dotnet-tools.json` und kommt mit `dotnet tool restore`.
- **DB-Prüfung und Migration nicht auf async umstellen:** `OpenAsync` von `MySql.Data` ignoriert Timeout und Abbruch, wenn die DB die Verbindung annimmt und nicht antwortet, und hängt ohne Ende. Deshalb nutzen die Prüfung das synchrone `Open()` und der Migrationsschritt das synchrone `Migrate()`. Der Test `Health_WithSilentDatabase_ReportsUnhealthyInTime` deckt die Prüfung ab.
