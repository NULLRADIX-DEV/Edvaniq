# Edvaniq – Deploy

Ein Deploy bringt alle Prozesse eines Commits auf `main` gemeinsam auf den VPS. Er startet nur von Hand, das ist die Freigabe. Automatisch rollt noch nichts aus.

## Ausrollen

1. Actions → „Deploy“ → „Run workflow“
2. Das Feld „commit“ leer lassen für den aktuellen Stand von `main`, oder einen älteren Commit von `main` eintragen.

Voraussetzung: Der Job „Container images“ hat für diesen Commit auf `main` die Images gepusht. Ausrollbar sind also nur Commits ab #118, die auch schon `deploy/` enthalten (ab #120).

## Ablauf

Der Workflow prüft, dass der Commit auf `main` liegt, und schickt `deploy/` aus genau diesem Commit per SSH an den Server. Der CI-Schlüssel darf dort nur dieses eine Kommando ausführen (`deploy/receive.sh`). Danach läuft `deploy/deploy.sh` im rootless Docker des Deploy-Benutzers:

1. **Prüfen:**
   - `.env` ist vorhanden und nur für den Deploy-Benutzer lesbar.
   - Die Compose-Datei ist gültig.
   - Alle Images des Commits sind gezogen.
   - Jedes Image trägt den Commit im Label `org.opencontainers.image.revision`.

   Scheitert hier etwas, bleibt der laufende Stand unverändert.
2. **Umschalten:** `docker compose up` für alle Dienste aus `deploy/compose.yml`
3. **Prüfen:** Nach 20 s laufen alle Container. Keiner ist neu gestartet, und alle kommen aus dem Commit.

Scheitert Schritt 2 oder 3, startet das Skript den vorherigen Stand wieder. Beim ersten Deploy gibt es keinen, dann fährt das Skript alles herunter. So laufen nie alte und neue Prozesse gemischt.

Der Server behält die Releases und Images des aktuellen und des vorherigen Commits. Ältere räumt der Deploy weg.

## Neuer Prozess

Er braucht einen Eintrag in `AppHost.cs` und einen Dienst mit dem Anker `*app` in `deploy/compose.yml`. Stimmen die beiden Listen nicht überein, schlägt der Job „Container images“ fehl.

## Secrets

- Compose liest Secrets nur aus der `.env` auf dem Server (`docs/secrets.md`). In `deploy/` steht kein Wert.
- SSH-Schlüssel, Ziel und Host-Schlüssel für den Deploy liegen als Secrets im Environment `production`. Das Environment darf nur `main` nutzen.
- Die Ausgabe des Deploys steht im öffentlichen Actions-Log. Deshalb gibt das Skript keine Pfade, Hosts oder Benutzer aus.
