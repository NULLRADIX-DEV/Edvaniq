# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Edvaniq – Hinweise für Claude

Dokumentation liegt im Nachbar-Repo `../EdvaniqDoc` (NULLRADIX-DEV/EdvaniqDoc). Architektur: `docs/architecture.md`.

## Befehle

```bash
dotnet build src/Aspire/Edvaniq.AppHost          # Backend komplett: alle Services, Worker, Gateway, Web
dotnet build Edvaniq.slnx                        # alles inkl. MAUI-App (braucht MAUI-Workload)
dotnet run --project src/Aspire/Edvaniq.AppHost  # startet MySQL-Container, alle Services + Aspire-Dashboard (Docker nötig)

dotnet test --project tests/Services/Identity/Edvaniq.Services.Identity.UnitTests            # ein Testprojekt
dotnet test --project tests/Services/Identity/Edvaniq.Services.Identity.UnitTests --filter-method "*Name*"   # einzelner Test (auch --filter-class, --filter-namespace)
dotnet format                                    # Codestil nach .editorconfig
```

- Tests laufen auf Microsoft Testing Platform (in `global.json` festgelegt) mit xUnit v3. Exit-Code 8 heißt „keine Tests gelaufen“, nicht „fehlgeschlagen“.
- SDK ist über `global.json` auf 10.0.401 gepinnt. `EnforceCodeStyleInBuild` ist aktiv, Stilverstöße tauchen also schon im Build auf.

## Architektur (Kurzfassung, Details in `docs/architecture.md`)

- **Microservices mit .NET Aspire.** `src/Aspire/Edvaniq.AppHost/AppHost.cs` ist die zentrale Stelle, an der alles verdrahtet wird: ein MySQL-Container (persistentes Volume, phpMyAdmin), eine Datenbank pro Service (`identitydb`, `planningdb` …), Services als `<name>-api` mit `WithReference(db).WaitFor(db)`. Worker nutzen die DB ihres Service. Das Gateway referenziert alle APIs, `web` referenziert nur das Gateway.
- **Ein Service = 5 Projekte** unter `src/Services/<Name>/`: `Domain` ← `Application` ← `Infrastructure` ← `Api`, dazu `Contracts` (Integration Events). Worker gibt es nur bei Content und Notifications. Von anderen Services darf nur `.Contracts` referenziert werden, niemals Domain, Application, Infrastructure oder Api.
- **Kommunikation:** asynchron über Integration Events (Message Broker, noch nicht ausgewählt), synchron nur HTTP über Service Discovery. Kein Service liest eine fremde DB.
- **ServiceDefaults** (`src/Aspire/Edvaniq.ServiceDefaults`) bindet jede App ein: OpenTelemetry, Health-Checks `/health` und `/alive` (nur in Development gemappt), Service Discovery, Standard-Resilience für HttpClient.
- **Clients:** Seiten werden einmal in `Client.UI` gebaut. Genutzt werden sie von `Edvaniq.Web.Client` (WASM, gehostet von `Edvaniq.Web`) und von `Edvaniq.App` (MAUI Blazor Hybrid). `Client.Core` spricht ausschließlich mit dem Gateway.
- **BuildingBlocks** enthalten nur Technik, keine Fachbegriffe.
- **Tests:** `UnitTests` referenzieren Domain und Application, `IntegrationTests` die Api des Service. `Edvaniq.EndToEndTests` startet den AppHost über `Aspire.Hosting.Testing`. `Edvaniq.ArchitectureTests` referenziert jedes Projekt, neue Projekte müssen dort ergänzt werden.
- **Stand:** Die Architektur steht als Skelett. Services enthalten nur `AddServiceDefaults()` bzw. `MapDefaultEndpoints()`, im Gateway ist noch kein YARP, BuildingBlocks und Client-Bibliotheken sind leer, Tests gibt es noch keine.

## Konventionen

- Paketversionen stehen nur in `Directory.Packages.props`. In den csproj-Dateien `PackageReference` immer ohne `Version` angeben.
- EF-Core-Provider ist `MySql.EntityFrameworkCore` (Oracle). Nicht Pomelo, das kann nur EF Core 9.
- Connection Strings bekommt ein Service von Aspire unter `ConnectionStrings:<service>db`.
- Neuer Service: Ordner mit den 5 Projekten und Tests unter `tests/Services/<Name>/` anlegen. Dann in `AppHost.cs` eintragen (`AddDatabase`, `AddService`, Gateway-`WithReference`), die Referenzen in `Edvaniq.ArchitectureTests` ergänzen und alles in `Edvaniq.slnx` aufnehmen.

## Stolperfallen

- Vor dem Bauen den laufenden AppHost stoppen, sonst sind DLLs gesperrt (MSB3027/MSB3021).
- Ohne Docker Desktop hängen alle Services beim `WaitFor` auf die Datenbank.
- Kommt das Aspire-Dashboard nicht an den Resource Service ran, liegt es an den Dev-Zertifikaten: `dotnet dev-certs https --clean` und danach `dotnet dev-certs https --trust`.

## Probleme und Herausforderungen loggen
Trifft Claude bei Planung oder Entwicklung auf ein Problem oder eine Herausforderung, kommt ein kurzer Eintrag ans Ende von `../EdvaniqDoc/notes.md`, Kapitel „Probleme und Herausforderungen“. Stil: knappe, lockere Notizen. Format:

```
## <Kurztitel>
Datum: TT.MM.JJJJ
Was:
Ursache:
Lösung:
Ergebnis:
```

- Nur Fakten eintragen, nichts erfinden. Ungelöstes bekommt `Ergebnis: offen`.
- Ist das Problem schon eingetragen, den bestehenden Eintrag aktualisieren statt einen neuen anzulegen.

## Projektdokumentation
`../EdvaniqDoc/Projektdokumentation.md` richtet sich nach dem Projekt, nicht umgekehrt. Anforderungen und Scope kommen aus `../EdvaniqDoc/Edvaniq.md` und der Roadmap.
