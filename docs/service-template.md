# Service-Vorlage

Neue Services entstehen nur aus der Vorlage in `templates/service/` (`dotnet new`, Kurzname `edvaniq-service`). Sie bringt die fünf Projekte, die Testprojekte und Health mit, das „lebendig“ und „bereit“ unterscheidet.

## Befehle

Alle im Repo-Root ausführen.

| Was | Befehl |
|---|---|
| Vorlage installieren, einmalig und nach jeder Änderung an ihr | `dotnet new install ./templates/service --force` |
| Installierte Vorlage anzeigen | `dotnet new list edvaniq-service` |
| Vorschau, legt nichts an | `dotnet new edvaniq-service -n <Name> --dry-run` |
| Service anlegen | `dotnet new edvaniq-service -n <Name>` |
| Tests des Service | `dotnet test --project tests/Services/<Name>/Edvaniq.Services.<Name>.IntegrationTests` |
| Vorlage entfernen | `dotnet new uninstall ./templates/service` |

`<Name>` in PascalCase, z. B. `LearningEngine`. Daraus werden `Edvaniq.Services.LearningEngine.*`, die Datenbank `learningenginedb` und der Name `learningengine-api` für AppHost und Compose.

## Was entsteht

```
src/Services/<Name>/
  Edvaniq.Services.<Name>.Api              Program.cs: AddServiceDefaults, AddInfrastructure, MapDefaultEndpoints
  Edvaniq.Services.<Name>.Application
  Edvaniq.Services.<Name>.Contracts
  Edvaniq.Services.<Name>.Domain
  Edvaniq.Services.<Name>.Infrastructure   InfrastructureExtensions.cs: DB-Prüfung für Health
tests/Services/<Name>/
  Edvaniq.Services.<Name>.UnitTests
  Edvaniq.Services.<Name>.IntegrationTests HealthTests.cs
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
2. `AppHost.cs` (`AddDatabase("<name>db")`, `AddService<…>("<name>-api", db)`, `WithReference` im Gateway) und `deploy/compose.yml` (Dienst `<name>-api`). Beides gehört zusammen, die CI prüft, dass die Listen passen. Erst eintragen, wenn die Datenbank des Service auf dem Server bereitsteht: Ohne DB wird der Container nie healthy, und der Deploy scheitert.

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
- **DB-Prüfung nicht auf `OpenAsync` umstellen:** `MySql.Data` ignoriert dort Timeout und Abbruch, wenn die DB die Verbindung annimmt und nicht antwortet, und hängt ohne Ende. Deshalb nutzt die Prüfung das synchrone `Open()`. Der Test `Health_WithSilentDatabase_ReportsUnhealthyInTime` deckt das ab.
