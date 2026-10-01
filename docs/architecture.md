# Edvaniq – Architektur

Microservices-Architektur, orchestriert mit .NET Aspire. Jeder Service ist ein eigener Bounded Context mit eigener Datenbank und eigenem Deployment.

## Ordnerstruktur

```
src/
  Aspire/          AppHost (lokale Orchestrierung), ServiceDefaults (Telemetrie, Health, Service Discovery)
  BuildingBlocks/  technische Basisbibliotheken – keine Fachlogik
  Gateway/         API-Gateway (YARP) – einziger Einstiegspunkt für Clients
  Services/<Name>/ ein Microservice = Api + Application + Domain + Infrastructure + Contracts (+ Worker)
  Clients/         Client.Core, Client.DesignSystem, Client.UI, Web, Web.Client, App (MAUI)
tests/
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

Abhängigkeiten zeigen immer nach innen: `Api → Infrastructure → Application → Domain`.

## Regeln

1. Ein Service referenziert von anderen Services **nur** `.Contracts`, niemals Domain/Application/Infrastructure/Api.
2. Kommunikation zwischen Services: **asynchron** über Integration Events (Message Broker), **synchron** nur per HTTP über Aspire Service Discovery.
3. Jeder Service besitzt seine eigene Datenbank. Kein Service liest die Datenbank eines anderen.
4. Clients kennen kein Backend-Projekt. `Client.Core` spricht ausschließlich mit dem Gateway (API-Clients per OpenAPI generiert).
5. BuildingBlocks enthalten nur technische Bausteine, keine Fachbegriffe.
6. Paketversionen stehen ausschließlich in `Directory.Packages.props`.

### Event-Konsum (wer hört auf wessen Contracts)

| Consumer | konsumiert von |
|---|---|
| Planning | Identity, Content, Knowledge, LearningEngine |
| Content | Identity |
| Knowledge | Identity, Content |
| Assessment | Identity, Content, Knowledge |
| Flashcards | Identity, Content, Knowledge, Assessment |
| LearningEngine | Identity, Planning, Knowledge, Assessment, Flashcards |
| Tutor | Identity, Content, Knowledge |
| Gamification | Identity, Planning, Assessment, Flashcards, LearningEngine |
| Analytics | Identity, Planning, Assessment, Flashcards, LearningEngine, Gamification |
| Notifications | Identity, Planning, Flashcards, Gamification |

## Clients

```
Edvaniq.Web (Host) ──► Edvaniq.Web.Client (WASM) ──┐
                                                    ├──► Client.UI ──► Client.Core (API, Auth, State, Offline)
Edvaniq.App (MAUI Blazor Hybrid) ───────────────────┘         └──► Client.DesignSystem (Tokens, Basis-Komponenten)
```

Seiten und Features werden **einmal** in `Client.UI` gebaut und laufen im Browser und in der App.

## Datenbank

- **MySQL**, ein Server, **eine Datenbank pro Service** (`identitydb`, `planningdb`, …). Worker nutzen die DB ihres Service.
- Lokal startet Aspire MySQL als Container (persistentes Volume) inkl. phpMyAdmin. Datenbanken werden automatisch angelegt.
- Services erhalten den Connection String per Aspire als `ConnectionStrings:<service>db`.
- EF-Core-Provider: `MySql.EntityFrameworkCore` (Oracle) unterstützt EF Core 10. `Pomelo.EntityFrameworkCore.MySql` ist aktuell nur bis EF Core 9 verfügbar.

## Starten

Voraussetzung: Docker Desktop (für den MySQL-Container).

```
dotnet run --project src/Aspire/Edvaniq.AppHost
```

Öffnet das Aspire-Dashboard mit MySQL, allen Services, Workern, Gateway und Web.

## Neuen Service anlegen

Neue Services entstehen aus der Vorlage in `templates/service/`. Im Repo-Root:

```
dotnet new install ./templates/service --force   # einmalig und nach jeder Änderung an der Vorlage
dotnet new edvaniq-service -n <Name>
```

- Das legt die fünf Projekte unter `src/Services/<Name>/` und UnitTests und IntegrationTests unter `tests/Services/<Name>/` an. Alle sieben Projekte trägt die Vorlage selbst in `Edvaniq.slnx` ein, damit baut und testet die CI sie mit.
- `-n` ist Pflicht, sonst heißt der Service wie der aktuelle Ordner.
- **Health:** `/alive` heißt lebendig und prüft nur, ob der Prozess antwortet. `/health` heißt bereit und prüft zusätzlich die eigene Datenbank (`ConnectionStrings:<name>db`). Fehlt der Connection String oder ist die DB nicht erreichbar, liefert `/health` 503 `Unhealthy`, `/alive` bleibt 200. Die IntegrationTests der Vorlage prüfen genau das.
- Die CI erzeugt bei jedem Lauf einen Service `Sample` aus der Vorlage und lässt seine Tests laufen. So bleibt die Vorlage lauffähig.

Danach von Hand:

1. `Edvaniq.ArchitectureTests`: Referenzen auf die neuen Projekte.
2. `AppHost.cs` (`AddDatabase("<name>db")`, `AddService<…>("<name>-api", db)`, `WithReference` im Gateway) und `deploy/compose.yml` (Dienst `<name>-api`). Beides gehört zusammen, die CI prüft, dass die Listen passen. Erst eintragen, wenn die Datenbank des Service auf dem Server bereitsteht: Ohne DB wird der Container nie healthy, und der Deploy scheitert.
