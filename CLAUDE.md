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
- Secrets nur in der Betriebsumgebung, nie in Repo, Client oder Log. Regeln stehen in `docs/secrets.md`. Die CI scannt die Git-History mit gitleaks.
- CI: `.github/workflows/ci.yml` läuft auf `ubuntu-24.04` ohne MAUI-Workload. Das App-Projekt nimmt die CI vor dem Build aus der Solution.
- Container-Images: Die CI baut für jeden Prozess im AppHost ein Image `ghcr.io/nullradix-dev/edvaniq/<resource>:sha-<commit>` mit dem SDK (`-t:PublishContainer`, keine Dockerfiles). Den Namen setzt `Directory.Build.props` aus dem Projektnamen. Gepusht wird nur auf `main` nach grüner CI, PR-Images bleiben im Runner. Die Images sind öffentlich wie das Repo, deshalb gehört nie ein Secret ins Image.
- Deploy: Der Workflow „Deploy“ rollt alle Prozesse eines `main`-Commits per Compose auf den VPS aus (`deploy/compose.yml`, `deploy/deploy.sh`). Er startet nur von Hand, das ist die Freigabe. Scheitert er, läuft der vorherige Stand weiter. Ein neuer Prozess braucht seinen Eintrag in `AppHost.cs` und einen Dienst in `deploy/compose.yml`, die CI prüft, dass beide Listen passen. Details in `docs/deploy.md`.
- Auf `main` nur per Pull Request mit grüner CI. Direkte Pushes sind gesperrt, die Regeln stehen in `docs/branch-protection.md`. Wer einen CI-Job umbenennt, muss die Pflicht-Checks im Ruleset `main-protect` nachziehen.
- Das Repo ist öffentlich. Betriebsdetails wie IPs, Ports, Serverpfade und Benutzer kommen nie hierher, sondern nach `../EdvaniqDoc/Betrieb.md`.
- Neuer Service: Ordner mit den 5 Projekten und Tests unter `tests/Services/<Name>/` anlegen. Dann in `AppHost.cs` eintragen (`AddDatabase`, `AddService`, Gateway-`WithReference`), die Referenzen in `Edvaniq.ArchitectureTests` ergänzen, API und Worker in `deploy/compose.yml` eintragen und alles in `Edvaniq.slnx` aufnehmen.

## Stolperfallen

- Vor dem Bauen den laufenden AppHost stoppen, sonst sind DLLs gesperrt (MSB3027/MSB3021).
- Ohne Docker Desktop hängen alle Services beim `WaitFor` auf die Datenbank.
- Kommt das Aspire-Dashboard nicht an den Resource Service ran, liegt es an den Dev-Zertifikaten: `dotnet dev-certs https --clean` und danach `dotnet dev-certs https --trust`.
- Dockerfiles für Blazor: `dotnet restore` nicht vorab nur mit der `.csproj` ausführen. Sonst lässt das Web-SDK `_framework/blazor.web.js` weg, die UI ist tot, und der Health-Check bleibt trotzdem grün. Im Build prüfen, ob die Datei im Publish-Output liegt. Bei NOOSE ist genau das passiert.

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

## Roadmap, Issues und Board

- **Roadmap:** `../EdvaniqDoc/Roadmap.md` enthält Analyse, MVP-Bewertung, Schätzung, Kosten, Meilensteine und alle Issues. Einzelbefunde der Analyse stehen in `Roadmap-Befunde.md`. Einzige Quelle der Issues ist `../EdvaniqDoc/roadmap-issues.json`. `MF001…` verweist auf die Master-Featureliste (Roadmap.md, Kapitel 3.6).
- **GitHub:**
  - Meilensteine: 8 in `NULLRADIX-DEV/Edvaniq`. M0 Fundament, M1 Walking Skeleton, M2 Konto/Material/KI-Themen, M3 Kern-Lernfluss, M4 Karten & Wiederholung, M5 Vollständigkeit, M6 Pilot. M6 schließt den MVP-Kern ab. M7 ist das optionale MVP-Plus.
  - Issues: 113 Epics (Issue-Typ `Epic`), darunter die Aufgaben als Sub-Issues (Typ `Feature` oder `Task`). Abhängigkeiten sind als native „blocked by“-Links gesetzt.
  - Board: https://github.com/orgs/NULLRADIX-DEV/projects/2 mit Auto-add und den Views Kanban, Meilensteine, Schul-Prototyp, Epics, Meine Aufgaben.
- **Labels:**
  - `area:<service>`: genau eines je Issue
  - `size:S|M|L`: menschliche Zeit ≤ 0,5 h / 0,5–2 h / > 2 h
  - `prio:must|should|could`
  - Querschnitt: `ai`, `security`, `legal`, `ux`, `test`, `docs`, `spike`
  - `schul-prototyp`: Demo-Pfad für die Abgabe, wird zuerst erledigt
  - `vor-echten-nutzern`: Pflicht, bevor externe Nutzer dazukommen
- **Issue-Aufbau:** Ziel · Akzeptanzkriterien · Abhängigkeiten · Bezug (MF, Epic, Meilenstein) · Nicht enthalten. Den versteckten Marker `<!-- edvaniq:M2-017 -->` im Body nie entfernen, darüber arbeitet das Push-Skript idempotent.
- **Issues beschreiben nur WAS, nie WIE.** Übernimmt Claude ein Issue:
  1. „Blocked by“ prüfen.
  2. Das Wie vorschlagen und besprechen.
  3. Erst nach Zustimmung umsetzen.
  4. Die Akzeptanzkriterien sind die Abnahme.
- **Neue Issues** folgen demselben Format und Labels. Arbeit außerhalb des MVP kommt nach Roadmap.md „Nach dem MVP“.
- **Push/Sync:** `../EdvaniqDoc/tools/push-roadmap.ps1` mit den Phasen `preflight|project|labels|milestones|issues|deps|verify` und dem Schalter `-DryRun`. Das Mapping liegt in `tools/push-map.jsonl`. GitHub CLI: `C:\Program Files\GitHub CLI\gh.exe`. GitHub erlaubt nur ca. 500 inhaltserzeugende Requests pro Stunde, also drosseln.

## MVP-Rahmen (Entscheidungen)

- **Team:** 2 erfahrene Entwickler, je 8–10 h/Woche. Claude setzt den Großteil um, gemessen 22× schneller. Die Entwickler steuern, reviewen und testen.
- **Termine:** Die Schul-Abgabe (Blockwoche 7, vor Ostern 2027) braucht nur einen lauffähigen Prototyp auf dem VPS (Label `schul-prototyp`). Der Großteil der App entsteht danach. Der MVP-Kern ist realistisch Okt–Dez 2027 fertig.
- **Services im MVP:**
  - Produktiv: Identity, Planning, Content (+ Worker), Knowledge, Assessment, LearningEngine, Flashcards (ab M4), Gateway und Web.
  - Nur Skelett, ohne Deployment: Analytics und Notifications.
  - Erst mit dem MVP-Plus: Gamification und Tutor.
- **Clients:** Web zuerst (responsive), nur online. Die MAUI-App bleibt kompilierbar, eine Android-Test-APK kommt erst im Plus.
- **Keine Vektor-DB im MVP:** Statt RAG werden Abschnitte Themen zugeordnet.
- **Offene Technikfragen** laufen als `spike`-Issues, z. B. Outbox für MySQL, Broker-Bibliothek mit freier Lizenz, KI-Anbieter. Technikvorschläge in Roadmap.md sind nicht verbindlich.
- **Datenschutz:** Viele Nutzer sind minderjährig. Datenschutz- und Jugendschutz-Issues (`legal`, `vor-echten-nutzern`) werden nie gestrichen.

## Server (Betrieb)

Die Server-Details (VPS, Docker, 1Panel, nginx, NOOSE, Deploy-Benutzer) stehen im privaten Nachbar-Repo, weil dieses Repo öffentlich ist. Neue Betriebsdetails wie IPs, Ports, Pfade, Benutzer und Zeitpläne nur dort eintragen, nie in dieses Repo.

@../EdvaniqDoc/Betrieb.md

## Arbeitsweise mit Claude

- Schätzungen immer in menschlicher Zeit: Faktor 22 auf die Umsetzung, Aufschlag nur für extern gebundene Arbeit (Anbieter, Verträge, Recht, VPS, Gerätetests). Nie mit „Entwickler + KI-Assistent“ rechnen.
- Agenten und Workflows: Erzeugende Agents laufen auf Sonnet, Kontrolle und Review auf Opus. Prompts schlank halten und Daten aus Dateien lesen lassen, damit die Token-Kosten niedrig bleiben.
- Nicht ohne Auftrag committen.
