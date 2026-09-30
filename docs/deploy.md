# Edvaniq – Deploy

Ein Deploy bringt alle Prozesse eines Commits auf `main` gemeinsam auf den Server. Er startet nur von Hand, das ist die Freigabe. Automatisch rollt noch nichts aus. Derselbe Workflow kann auch auf den vorherigen Stand zurückgehen ([Rollback](#rollback)).

Auf dem Server läuft Edvaniq als eigene App einer gemeinsamen Plattform:
- eigener Linux-Benutzer
- eigener rootless Docker
- Obergrenze für Speicher und CPU

So kann Edvaniq keine andere App auf dem Server stören, und umgekehrt. Ausgerollt wird wie bei jeder App der Plattform: Der Workflow „Deploy“ ist eine Kopie der gemeinsamen Vorlage, die Deploy-Logik liegt auf dem Server.

## Ausrollen

1. Actions → „Deploy“ → „Run workflow“
2. „environment“ bleibt `production`. Das Feld „commit“ leer lassen für den aktuellen Stand von `main`, oder einen älteren Commit von `main` eintragen.

Voraussetzung: Der Job „Container images“ hat für diesen Commit auf `main` die Images gepusht.

## Ablauf

Der Workflow prüft, dass der Commit auf `main` liegt, und schickt nur `deploy/compose.yml` aus genau diesem Commit per SSH an den Server. Der Deploy-Schlüssel darf dort nur den Empfang ausführen, und der kennt nur Deploy und Rollback. Danach läuft der Deploy der Plattform als App-Benutzer:

1. **Prüfen:**
   - Die Compose-Datei ist gültig und hält die Regeln der Plattform ein: keine Host-Mounts, kein Host-Netz, keine zusätzlichen Rechte, Images nur aus erlaubten Quellen, Ports nur lokal.
   - Alle Images des Commits liegen auf dem Server. Fehlende werden gezogen.
   - Jedes eigene Image trägt den Commit im Label `org.opencontainers.image.revision`.

   Scheitert hier etwas, bleibt der laufende Stand unverändert.
2. **Umschalten:** `docker compose up` für alle Dienste aus `deploy/compose.yml`. Das Image-Tag `sha-<commit>` setzt der Server über `APP_COMMIT`.
3. **Prüfen:** Nach 20 s laufen alle Container. Keiner ist neu gestartet, und alle kommen aus dem Commit.

Scheitert Schritt 2 oder 3, startet der Server den vorherigen Stand wieder. Beim ersten Deploy gibt es keinen, dann fährt er alles herunter. So laufen nie alte und neue Prozesse gemischt.

Der Server behält die Releases und Images des aktuellen und des vorherigen Commits, ältere räumt der Deploy weg. Sobald Edvaniq eine Datenbank hat, sichert der Server sie vor jedem Umschalten.

## Rollback

Für den Fall, dass der neue Stand zwar läuft, aber fachlich kaputt ist. Scheitert der Deploy selbst, braucht es keinen Rollback, denn dann stellt der Server den vorherigen Stand schon von selbst wieder her.

1. Actions → „Deploy“ → „Run workflow“
2. „rollback“ anhaken und „commit“ leer lassen
3. Der Lauf ist nach 1–2 Minuten grün. In der Zusammenfassung steht der Commit, der jetzt läuft.

Der Server startet den vorherigen Stand mit dessen Compose-Datei, es gelten also dieselben Prüfungen wie beim Deploy. Scheitert der Rollback, läuft der aktuelle Stand weiter. Die Images des vorherigen Stands liegen noch auf dem Server, deshalb braucht der Rollback die Registry nicht.

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

- Die Container lesen Secrets nur aus `app.env` auf dem Server (`docs/secrets.md`). In `deploy/` steht kein Wert.
- SSH-Schlüssel, Ziel und Host-Schlüssel für den Deploy liegen als Secrets im Environment `production`. Das Environment darf nur `main` nutzen.
- Die Ausgabe des Deploys steht im öffentlichen Actions-Log. Deshalb gibt der Server keine Pfade, Hosts oder Benutzer aus.
