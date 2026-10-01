# Service-Vorlage

Neue Services entstehen nur aus der Vorlage in `templates/service/` (`dotnet new`, Kurzname `edvaniq-service`). Sie bringt die fünf Projekte, die Testprojekte, Health, das „lebendig“ und „bereit“ unterscheidet, und eine eigene Datenbank mit Migrationen mit.

## Befehle

Alle im Repo-Root ausführen.

| Was | Befehl |
|---|---|
| Vorlage installieren, einmalig und nach jeder Änderung an ihr | `dotnet new install ./templates/service --force` |
| Installierte Vorlage anzeigen | `dotnet new list edvaniq-service` |
| Vorschau, legt nichts an | `dotnet new edvaniq-service -n <Name> --dry-run` |
| Service anlegen | `dotnet new edvaniq-service -n <Name>` |
| Tests des Service | `dotnet test --project tests/Services/<Name>/Edvaniq.Services.<Name>.IntegrationTests` |
| `dotnet ef` bereitstellen, einmalig je Klon | `dotnet tool restore` |
| Migration anlegen, braucht keine DB | `dotnet ef migrations add <Migration> --project src/Services/<Name>/Edvaniq.Services.<Name>.Infrastructure` |
| Vorlage entfernen | `dotnet new uninstall ./templates/service` |

`<Name>` in PascalCase, z. B. `LearningEngine`. Daraus werden `Edvaniq.Services.LearningEngine.*`, die Datenbank `learningenginedb` und der Name `learningengine-api` für AppHost und Compose.

## Was entsteht

```
src/Services/<Name>/
  Edvaniq.Services.<Name>.Api              Program.cs: AddServiceDefaults, AddInfrastructure, MapDefaultEndpoints, Befehl migrate
  Edvaniq.Services.<Name>.Application
  Edvaniq.Services.<Name>.Contracts
  Edvaniq.Services.<Name>.Domain
  Edvaniq.Services.<Name>.Infrastructure   InfrastructureExtensions.cs: DbContext und DB-Prüfung für Health
                                           <Name>DbContext.cs, Migrations/ mit der leeren Migration Initial
tests/Services/<Name>/
  Edvaniq.Services.<Name>.UnitTests
  Edvaniq.Services.<Name>.IntegrationTests HealthTests.cs, MigrationTests.cs
```

- Alle sieben Projekte stehen danach in `Edvaniq.slnx`, die CI baut und testet sie also mit.
- Die Ports in `launchSettings.json` wählt die Vorlage je Service neu.

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

## Datenbank und Migrationen

- **Eigene DB, eigener Benutzer:** Jeder Service hat die Datenbank `<name>db` und den Benutzer `<name>`, der nur auf sie darf. Lokal legt der AppHost beides an (`AddServiceDatabase`), das Passwort erzeugt er einmal und legt es in seinen User Secrets ab. Der Service bekommt `ConnectionStrings:<name>db` mit diesem Benutzer, nie mit root. Auf dem Server kommen DB und Benutzer mit der ersten Service-DB dort.
- **Schema nur per Migration:** EF Core mit `MySql.EntityFrameworkCore`, die Migrationen liegen in `Infrastructure/Migrations`. Nach der leeren Migration `Initial` enthält die DB nur die Tabelle `__EFMigrationsHistory`.
- **Migrieren ist ein eigener Schritt:** `dotnet Edvaniq.Services.<Name>.Api.dll migrate` spielt alle fehlenden Migrationen ein und endet mit Exit-Code 0, bei einem Fehler mit 1. Ein zweiter Lauf findet nichts zu tun und endet auch mit 0. Ein normaler Start migriert nie. Lokal übernimmt das die Ressource `<name>-migrate`, die API startet erst, wenn sie fertig ist.
- **Neue Migration:** Modell in `<Name>DbContext` ändern, dann `dotnet ef migrations add <Migration> --project …Infrastructure` (siehe [Befehle](#befehle)). Das braucht keine Datenbank. Der Test `Migrations_MatchTheModel` schlägt fehl, wenn das Modell ohne Migration geändert wurde.
- **Grenze:** MySQL rollt DDL nicht zurück. Bricht eine Migration mittendrin ab, bleibt sie halb angewendet, und nur die Sicherung vor der Migration hilft. Deshalb Migrationen klein halten.

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
   var <name>Migration = builder.AddMigration<Projects.Edvaniq_Services_<Name>_Api>("<name>-migrate", <name>Db);
   var <name>Api = AddService<Projects.Edvaniq_Services_<Name>_Api>("<name>-api", <name>Db)
       .WaitForCompletion(<name>Migration);
   ```

3. `deploy/compose.yml`: Dienst `<name>-api`. AppHost und Compose gehören zusammen, die CI prüft, dass die Listen passen. Erst eintragen, wenn die Datenbank des Service auf dem Server bereitsteht: Ohne DB wird der Container nie healthy, und der Deploy scheitert.

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
- **`dotnet ef` fehlt:** Das Tool steht im Manifest `dotnet-tools.json` und kommt mit `dotnet tool restore`.
- **DB-Prüfung und Migration nicht auf async umstellen:** `OpenAsync` von `MySql.Data` ignoriert Timeout und Abbruch, wenn die DB die Verbindung annimmt und nicht antwortet, und hängt ohne Ende. Deshalb nutzen die Prüfung das synchrone `Open()` und der Migrationsschritt das synchrone `Migrate()`. Der Test `Health_WithSilentDatabase_ReportsUnhealthyInTime` deckt die Prüfung ab.
