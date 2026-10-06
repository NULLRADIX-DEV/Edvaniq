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

## Regeln

Diese Regeln halten die Services auseinander:

1. Von einem anderen Service referenziert ein Service nur dessen `.Contracts`, niemals Domain, Application, Infrastructure oder Api.
2. Services sprechen asynchron über Integration Events miteinander, über einen Message Broker. Synchron geht nur HTTP über die Service Discovery von Aspire.
3. Jeder Service besitzt seine eigene Datenbank. Kein Service liest die Datenbank eines anderen.
4. Clients kennen kein Backend-Projekt. `Client.Core` spricht nur mit dem Gateway, die API-Clients entstehen per OpenAPI.
5. BuildingBlocks enthalten nur technische Bausteine, keine Fachbegriffe.
6. Paketversionen stehen nur in `Directory.Packages.props`.
7. Jede Anfrage an einen Service braucht ein gültiges Token. Der Nutzer kommt nur aus dem Token (`sub`), nie aus der Anfrage ([Service-Vorlage](service-template.md#token-prüfung)).

Wer auf wessen Integration Events hört:

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

## Gateway

Clients sprechen nie direkt mit einem Service, sondern immer mit dem Gateway, und das reicht die Anfrage weiter. Welcher Pfad zu welchem Service führt, steht nur in der Konfiguration, im Abschnitt `ReverseProxy` der `appsettings.json` des Gateways. Jeder Service hat dort einen eigenen Pfad, den das Gateway vor dem Weiterreichen abschneidet. `POST /planning/ping` kommt bei Planning also als `POST /ping` an.

Als Ziel steht dort ein Name der Service Discovery, etwa `https+http://planning-api`. Lokal löst Aspire ihn auf, auf dem Server die Variable `services__planning-api__http__0` beim Dienst `gateway` in `deploy/compose.yml`. Das Token reicht das Gateway unverändert durch, geprüft wird es im Service.

Kennt das Gateway einen Pfad nicht, antwortet es mit 404. Ist der Service nicht erreichbar, kommt 502. Bleibt er länger stumm, als `HttpRequest:ActivityTimeout` am Cluster erlaubt, kommt 504. Der Wert liegt unter den 10 Sekunden, die ein Aufrufer mit der Standard-Resilience je Versuch wartet, sonst gäbe der vorher auf. In allen drei Fällen steht im Body ein Problem nach RFC 9457 (`application/problem+json`), das den Grund nennt. Eine gescheiterte Anfrage wiederholt das Gateway nicht von selbst, weil ein `POST` sonst doppelt ankommen könnte.

## Clients

```
Edvaniq.Web (Host) ──► Edvaniq.Web.Client (WASM) ──┐
                                                    ├──► Client.UI ──► Client.Core (API, Auth, State, Offline)
Edvaniq.App (MAUI Blazor Hybrid) ───────────────────┘         └──► Client.DesignSystem (Tokens, Basis-Komponenten)
```

Seiten und Features entstehen einmal in `Client.UI` und laufen dann im Browser und in der App.

## Datenbank

Alle Services nutzen MySQL, einen Server mit einer Datenbank pro Service (`identitydb`, `planningdb` und so weiter). Ein Worker nutzt die Datenbank seines Service. Lokal startet Aspire MySQL als Container mit persistentem Volume und phpMyAdmin und legt die Datenbanken selbst an.

Den Connection String bekommt ein Service von Aspire als `ConnectionStrings:<service>db`, mit einem eigenen DB-Benutzer, der nur auf die eigene Datenbank darf (`AddServiceDatabase` im AppHost). Daten eines Nutzers (`IOwnedByUser`) sieht nur dieser Nutzer, weil der DbContext jedes Service von `ServiceDbContext` erbt. Der schränkt jede Abfrage auf den Nutzer aus dem Token ein ([Service-Vorlage](service-template.md#daten-je-nutzer)).

Das Schema ändert sich nur über EF-Core-Migrationen, und Migrieren ist ein eigener Schritt (`<Api>.dll migrate`), nie Teil des Starts ([Service-Vorlage](service-template.md#datenbank-und-migrationen)). Als Provider dient `MySql.EntityFrameworkCore` von Oracle, weil er EF Core 10 kann. `Pomelo.EntityFrameworkCore.MySql` gibt es bisher nur bis EF Core 9.

## Starten

Du brauchst Docker Desktop für den MySQL-Container. Dann startet ein Befehl alles:

```
dotnet run --project src/Aspire/Edvaniq.AppHost
```

Es öffnet sich das Aspire-Dashboard mit MySQL, allen Services, Workern, Gateway und Web.

## Neuen Service anlegen

Neue Services entstehen nur aus der Vorlage in `templates/service/`. Im Repo-Root:

```
dotnet new install ./templates/service --force   # einmalig und nach jeder Änderung an der Vorlage
dotnet new edvaniq-service -n <Name>
```

Was dabei entsteht und was du danach von Hand erledigst, steht in der [Service-Vorlage](service-template.md).
