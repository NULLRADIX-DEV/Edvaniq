# Secrets

Ein Secret ist alles, womit sich jemand als Edvaniq ausgeben oder an fremde Daten kommen kann: Passwörter, Connection Strings mit Passwort, Tokens, API-Schlüssel und private Zertifikate. Sie existieren nur in der Umgebung, in der der Code läuft. Im Repo, im Client, im Image und im Log haben sie nichts verloren.

## Lokal

Für die Entwicklung musst du keine Secrets anlegen. Aspire verwaltet die Passwörter seiner Container selbst. Die Passwörter der DB-Benutzer erzeugt der AppHost beim ersten Start und legt sie in seinen User Secrets ab. Auch ein Dev-Token von `dotnet user-jwts` speichert seinen Signaturschlüssel in den User Secrets der Api, und der gilt nur in Development. Brauchst du einen eigenen Wert, etwa einen API-Schlüssel zum Ausprobieren, setzt du ihn mit `dotnet user-secrets` am jeweiligen Projekt.

## CI und Server

In GitHub liegen Secrets im Repo oder im Environment `production`, das nur `main` nutzen darf. Für die Container-Registry reicht das automatische `GITHUB_TOKEN`.

Auf dem Server bekommt jeder Container seine Werte erst beim Start, als Umgebungsvariablen aus der Betriebsumgebung. Gemeinsame Werte sehen alle Container. Den Connection String einer Datenbank bekommt aber nur der Service, dem sie gehört, und das Passwort des DB-Admins sieht kein Service.

Im Code liest ein Service ein Secret nur aus der Konfiguration, zum Beispiel `ConnectionStrings__planningdb`. Ein Default im Code wäre ein Secret im Repo.

## Regeln

- `appsettings*.json` enthalten keine Secrets, auch keine Beispielpasswörter. Vorlagen wie `.env.example` enthalten nur Platzhalter.
- `.env`, `.env.*`, `secrets/`, `*.pfx`, `*.key` und `*.pem` stehen in `.gitignore`.
- Alles unter `src/Clients/**/wwwroot` und alles in `Edvaniq.Web.Client` landet im Browser. Dort steht nur, was jeder sehen darf, etwa die Adresse des Gateways.
- Die Images in GHCR sind öffentlich wie das Repo. Ein Secret gehört deshalb weder in `ContainerEnvironmentVariable` noch in eine `appsettings*.json` oder eine andere Datei im Image.
- Nichts loggen, was ein Secret enthalten kann: keine Konfiguration, keine Connection Strings, keine Header mit Tokens. `EnableSensitiveDataLogging` in EF Core gibt es nur in Development.

## Wenn doch etwas durchrutscht

Ein committetes Secret ist verbrannt. Wechsle den Wert sofort beim Anbieter oder in der Datenbank. Es nur aus der History zu löschen reicht nicht, denn Forks, Caches und Klone haben es längst.

## Wie geprüft wird

Bei jedem Pull Request und jedem Push auf `main` durchsucht der CI-Job `secret-scan` die ganze Git-History mit [gitleaks](https://github.com/gitleaks/gitleaks), und ein Fund macht die CI rot. Die `.gitleaks.toml` nimmt die Standardregeln und verbietet zusätzlich öffentliche IPv4-Adressen, weil Server-Adressen nicht ins Repo gehören. Private Netze, Loopback und die Adressen für Doku-Beispiele sind erlaubt.

Funde aus der Zeit vor dem Scan stehen mit ihrem Fingerprint in `.gitleaksignore`. Ein neuer Fund kommt dort nie hinein. Entferne stattdessen den Wert, und wechsle ihn, wenn es ein Secret war.

GitHub prüft zusätzlich selbst. Secret Scanning mit Push Protection lehnt einen Push mit einem erkannten Secret schon beim Hochladen ab.

Selbst nachsehen kannst du so:

```
gitleaks git --redact .                                     # vor dem Push
dotnet publish src/Clients/Edvaniq.Web -c Release -o out    # alles, was im Browser landet …
gitleaks dir --redact out                                   # … durchsuchen
```

Ob ein Service beim Start etwas ausplaudert, zeigt ein Kanarienwert. Starte ihn mit `ConnectionStrings__planningdb="...;Password=CANARY_123"`, und durchsuche danach die Logs nach `CANARY`.
