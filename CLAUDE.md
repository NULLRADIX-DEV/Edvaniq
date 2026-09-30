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

- **Server:** Contabo-VPS `62.169.28.155` (Ubuntu 24.04, 6 vCPU, 12 GB RAM, 4 GB Swap). SSH nur per Schlüssel als `root`. Gehärtet mit ufw (22/80/443), fail2ban und unattended-upgrades.
- **Docker CE + Compose** aus dem offiziellen Repo, Updates über unattended-upgrades. `/etc/docker/daemon.json`:
  - `"ip": "127.0.0.1"`: Veröffentlichte Ports landen nur auf Loopback, weil Docker ufw umgeht. Nie `0.0.0.0` explizit veröffentlichen.
  - Log-Driver `local` (20 MB × 5)
  - `live-restore`
- **Dashboard:** 1Panel (Monitoring, Konsole, Dateien, Container), nur über Tailscale erreichbar (ufw nur auf `tailscale0`, 1Panel-IP-Filter `100.64.0.0/10`, 2FA). Keine 1Panel-„Websites“ und kein OpenResty, denn 80/443 gehören dem nginx des Hosts. Ports nie über die 1Panel-Firewall öffnen. 1Panel legt beim Start selbst eine öffentliche ufw-Regel für seinen Port an. Deshalb verwirft eine Regel in `/etc/ufw/before.rules` und `before6.rules` den Port 32085 außer auf `tailscale0`, und die greift vor allen ufw-Regeln.
- **Neustart:** 1Panel-Cronjobs „Neustart bei Bedarf“ (täglich, nur wenn `/var/run/reboot-required` existiert) und ein geplanter Neustart jeden Montag. Nach einem Neustart kommt alles von selbst wieder hoch (getestet am 30.09.). `noose` und `noose-demo` scheitern dabei einmal an der noch nicht bereiten DB und starten automatisch neu.
- **Geteilt mit** (alles als Container, Compose-Projekte unter `/opt/<name>`, Images privat in GHCR, gebaut per GitHub Action; der Server ist per `docker login ghcr.io` mit einem `read:packages`-PAT angemeldet):
  - NOOSE (`/opt/noose`):
    - `noose` auf 127.0.0.1:5000, `noose-demo` auf 127.0.0.1:5001
    - MariaDB 10.11 als `noose-db` auf 127.0.0.1:3306
    - alle im Host-Netz
    - Backup über 1Panel-Cronjobs („Backup database“ auf dem Remote-Eintrag `noose_mariadb`): Prod um 04:30 (30 Kopien), Demo um 04:45 (7 Kopien), nach `/opt/1panel/backup/database/mariadb/`. `/opt/noose/backup.sh` bleibt als manuelle Reserve.
    - Achtung: In 1Panel löscht „Delete“ bei einer Datenbank die echte DB auf dem Server. Das ist am 30.09. passiert, die Wiederherstellung kam aus dem Backup.
  - NULLRADIX (`/opt/nullradix`): nginx-Container auf 127.0.0.1:8080
- **nginx** ist der gemeinsame Reverse-Proxy mit Let's Encrypt (certbot-Timer). `edvaniq.nullradix.de` zeigt per A-Record auf den Server (kein AAAA). Die nginx-Site `edvaniq` hat ein Let's-Encrypt-Zertifikat und leitet HTTP auf HTTPS um. Solange die App fehlt, liefert sie eine Platzhalterseite aus `/var/www/edvaniq` (`noindex`). Später proxyt die Site auf das Gateway.
- **Edvaniq darf NOOSE nie beeinträchtigen:**
  - eigene MySQL als Container, nicht die MariaDB von NOOSE; kein Port 3306, denn den belegt `noose-db`
  - Speicherlimit für jeden Container
  - eigene Backups
  - Deploys fassen keine NOOSE-Dateien und keine NOOSE-Dienste an, auch nicht `/opt/noose` oder die NOOSE-Container
  - Edvaniq kommt nach `/opt/edvaniq`, mit eigenem Compose-Projekt und eigenem Docker-Netz
- **Deploy-Benutzer `deploy`** (uid 1001):
  - Anmeldung nur per SSH-Schlüssel, Passwort gesperrt
  - nicht in `sudo` und nicht in `docker`, denn Zugriff auf den Docker-Socket wäre gleichbedeutend mit Root
  - ihm gehört `/opt/edvaniq`; `/opt/noose` und `/opt/nullradix` stehen auf `750 root` und sind für ihn gesperrt
  - Wie er Container startet, ohne Root zu sein, wird beim Deploy-Issue festgelegt.

## Arbeitsweise mit Claude

- Schätzungen immer in menschlicher Zeit: Faktor 22 auf die Umsetzung, Aufschlag nur für extern gebundene Arbeit (Anbieter, Verträge, Recht, VPS, Gerätetests). Nie mit „Entwickler + KI-Assistent“ rechnen.
- Agenten und Workflows: Erzeugende Agents laufen auf Sonnet, Kontrolle und Review auf Opus. Prompts schlank halten und Daten aus Dateien lesen lassen, damit die Token-Kosten niedrig bleiben.
- Nicht ohne Auftrag committen.
