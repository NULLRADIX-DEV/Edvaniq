# Edvaniq – Secrets

Secrets sind Passwörter, Connection Strings mit Passwort, Tokens, API-Schlüssel und Zertifikate. Sie liegen nur in der Betriebsumgebung: nie im Repo, nie im Client, nie im Log.

## Wo Secrets liegen

| Umgebung | Ort |
|---|---|
| Lokal | Aspire verwaltet die Passwörter seiner Container selbst. Eigene Werte kommen per `dotnet user-secrets` an das jeweilige Projekt. |
| CI | GitHub-Secrets des Repos. Für GHCR reicht das automatische `GITHUB_TOKEN`. SSH-Schlüssel, Ziel und Host-Schlüssel für den Deploy liegen im Environment `production`, das nur `main` nutzen darf. |
| VPS | Datei `app.env` im Verzeichnis der App auf dem Server, nur für den App-Benutzer lesbar (Modus `600`). Die Container lesen sie per `env_file`, ohne sie bricht der Deploy ab. Pfad und Benutzer stehen in `EdvaniqDoc/Betrieb.md`. |

Services bekommen Secrets nur über Konfiguration (Umgebungsvariablen), z. B. `ConnectionStrings__planningdb`. Im Code steht nie ein Secret, auch kein Default.

## Regeln

- `appsettings*.json` im Repo enthalten keine Secrets, auch keine Beispielpasswörter. Vorlagen wie `.env.example` enthalten nur Platzhalter.
- `.env`, `.env.*`, `secrets/`, `*.pfx`, `*.key` und `*.pem` stehen in `.gitignore`.
- Alles unter `src/Clients/**/wwwroot` und alles in `Edvaniq.Web.Client` wird an den Browser ausgeliefert. Dort stehen nur öffentliche Werte, z. B. die Gateway-URL.
- Die Container-Images in GHCR sind öffentlich. Kein Secret per `ContainerEnvironmentVariable`, `appsettings*.json` oder als Datei ins Image. Secrets kommen erst beim Start aus der Betriebsumgebung dazu.
- Keine Konfiguration, keine Connection Strings und keine Header mit Tokens loggen. `EnableSensitiveDataLogging` in EF Core nur in Development.
- Ist ein Secret doch einmal committet, gilt es als verbrannt: Wert beim Anbieter bzw. in der DB sofort wechseln. Nur aus der History löschen reicht nicht.

## Prüfung

- Die CI (Job `secret-scan`) prüft bei jedem PR und jedem Push auf `main` die komplette Git-History mit [gitleaks](https://github.com/gitleaks/gitleaks). Ein Fund macht die CI rot.
- GitHub Secret Scanning mit Push Protection ist aktiv. Pushes mit erkannten Secrets lehnt GitHub schon beim Push ab.
- Lokal vor dem Push: `gitleaks git --redact .`
- Client-Stichprobe: `dotnet publish src/Clients/Edvaniq.Web -c Release -o out`, danach `gitleaks dir --redact out`.
- Startlogs: Service mit einem Canary-Wert starten, z. B. `ConnectionStrings__planningdb="...;Password=CANARY_123"`, und die Logs nach `CANARY` durchsuchen.
