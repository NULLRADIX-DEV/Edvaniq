# Edvaniq – Deploy

Ein Deploy bringt alle Prozesse eines Commits auf `main` gemeinsam auf den VPS. Er startet nur von Hand, das ist die Freigabe. Automatisch rollt noch nichts aus. Derselbe Workflow kann auch auf den vorherigen Stand zurückgehen ([Rollback](#rollback)).

## Ausrollen

1. Actions → „Deploy“ → „Run workflow“
2. Das Feld „commit“ leer lassen für den aktuellen Stand von `main`, oder einen älteren Commit von `main` eintragen.

Voraussetzung: Der Job „Container images“ hat für diesen Commit auf `main` die Images gepusht. Ausrollbar sind also nur Commits ab #118, die auch schon `deploy/` enthalten (ab #120).

## Ablauf

Der Workflow prüft, dass der Commit auf `main` liegt, und schickt `deploy/` aus genau diesem Commit per SSH an den Server. Der CI-Schlüssel darf dort nur `deploy/receive.sh` ausführen, und das kennt nur Deploy und Rollback. Danach läuft `deploy/deploy.sh` als Deploy-Benutzer im Docker des Servers:

1. **Prüfen:**
   - `.env` ist vorhanden und nur für den Deploy-Benutzer lesbar.
   - Die Compose-Datei ist gültig.
   - Alle Images des Commits liegen auf dem Server. Fehlende werden gezogen.
   - Jedes Image trägt den Commit im Label `org.opencontainers.image.revision`.

   Scheitert hier etwas, bleibt der laufende Stand unverändert.
2. **Umschalten:** `docker compose up` für alle Dienste aus `deploy/compose.yml`
3. **Prüfen:** Nach 20 s laufen alle Container. Keiner ist neu gestartet, und alle kommen aus dem Commit.

Scheitert Schritt 2 oder 3, startet das Skript den vorherigen Stand wieder. Beim ersten Deploy gibt es keinen, dann fährt das Skript alles herunter. So laufen nie alte und neue Prozesse gemischt.

Der Server behält die Releases und Images des aktuellen und des vorherigen Commits. Ältere räumt der Deploy weg.

Den Docker teilt sich Edvaniq mit anderen Apps auf dem Server. Der Deploy fasst deshalb nur das Compose-Projekt `edvaniq` und die Edvaniq-Images an. Jeder Container hat ein eigenes Speicherlimit, und zusammen laufen alle in einer cgroup mit Obergrenze (`cgroup_parent` in `deploy/compose.yml`). So kann Edvaniq die anderen Apps nie verdrängen.

## Rollback

Für den Fall, dass der neue Stand zwar läuft, aber fachlich kaputt ist. Scheitert der Deploy selbst, braucht es keinen Rollback, denn dann stellt das Skript den vorherigen Stand schon von selbst wieder her.

1. Actions → „Deploy“ → „Run workflow“
2. „rollback“ anhaken und „commit“ leer lassen
3. Der Lauf ist nach 1–2 Minuten grün. In der Zusammenfassung steht der Commit, der jetzt läuft.

Der Server startet den vorherigen Stand mit dessen eigenem `deploy.sh`, es gelten also dieselben Prüfungen wie beim Deploy. Scheitert der Rollback, läuft der aktuelle Stand weiter. Die Images des vorherigen Stands liegen noch auf dem Server, deshalb braucht der Rollback die Registry nicht.

Danach:

- Der fehlerhafte Stand ist jetzt der vorherige. Ein zweiter Rollback ginge also wieder nach vorn.
- Ein Deploy ohne Commit rollt den aktuellen Stand von `main` aus, also wieder den fehlerhaften. Erst deployen, wenn der Fix auf `main` ist.
- Zu einem älteren Stand als dem vorherigen geht es mit einem normalen Deploy, den Commit trägt man ins Feld „commit“ ein. Die Images kommen dann aus der Registry.

Grenzen:

- Ein Rollback macht Datenmigrationen nicht rückgängig.
- Ist GitHub nicht erreichbar, steht der Notfallweg in `EdvaniqDoc/Betrieb.md`.

## Neuer Prozess

Er braucht einen Eintrag in `AppHost.cs` und einen Dienst mit dem Anker `*app` in `deploy/compose.yml`. Stimmen die beiden Listen nicht überein, schlägt der Job „Container images“ fehl.

## Secrets

- Compose liest Secrets nur aus der `.env` auf dem Server (`docs/secrets.md`). In `deploy/` steht kein Wert.
- SSH-Schlüssel, Ziel und Host-Schlüssel für den Deploy liegen als Secrets im Environment `production`. Das Environment darf nur `main` nutzen.
- Die Ausgabe des Deploys steht im öffentlichen Actions-Log. Deshalb gibt das Skript keine Pfade, Hosts oder Benutzer aus.
