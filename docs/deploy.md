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
2. **Umschalten:** `docker compose up` für alle Dienste aus `deploy/compose.yml`. Das Image-Tag `sha-<commit>` setzt der Server über `APP_COMMIT`. Der Server wartet, bis jeder Container mit [Health-Check](#health) healthy ist.
3. **Prüfen:** Nach 20 s laufen alle Container. Keiner ist neu gestartet, und alle kommen aus dem Commit.

Scheitert Schritt 2 oder 3, startet der Server den vorherigen Stand wieder. Beim ersten Deploy gibt es keinen, dann fährt er alles herunter. So laufen nie alte und neue Prozesse gemischt.

Der Server behält die Releases und Images des aktuellen und des vorherigen Commits, ältere räumt der Deploy weg. Sobald Edvaniq eine Datenbank hat, sichert der Server sie vor jedem Umschalten.

## Speicher

Jeder Container hat eine eigene Speichergrenze und keinen Swap (`mem_limit` und `memswap_limit` in `deploy/compose.yml`):

| Prozesse | Grenze |
|---|---|
| alle, die im MVP produktiv werden (Identity, Planning, Content mit Worker, Knowledge, Assessment, Flashcards, LearningEngine, Gateway, Web) | 256 MB |
| die im MVP nur Skelett bleiben (Analytics, Notifications mit Worker, Gamification, Tutor), Anker `*skeleton` | 128 MB |

- **Summe:** 10 × 256 + 5 × 128 = 3200 MB. Das liegt deutlich unter der Obergrenze der App auf dem Server (4 GB ohne Swap). Die Reserve braucht Docker selbst.
- **Überschreitung:** Braucht ein Container mehr als seine Grenze, beendet der Kernel nur diesen Container, und Docker startet ihn neu (`restart: unless-stopped`). Die anderen Prozesse laufen weiter.
- **.NET kennt die Grenze:** Es begrenzt seinen Heap auf 75 % der Container-Grenze. Jeder Prozess schreibt beim Start `GC memory limit: <n> MiB` ins Log, bei 256 MB sind das 192 MiB.
- **Anheben:** Braucht ein Prozess mehr, wird seine Grenze im Dienst überschrieben. Die Summe muss unter der Obergrenze der App bleiben.

## Health

- **Wo:** Jeder Prozess mit HTTP meldet `/health` (bereit) und `/alive` (lebendig). Außerhalb von Development antworten beide nur auf dem internen Port 8081. Der wird nie veröffentlicht, also erreicht ihn nur der Container selbst und das Netz der App. Auf dem App-Port 8080, an den später der Proxy weiterleitet, antworten beide Pfade mit 404.
- **Kein Umweg über den Host-Header:** Geprüft wird der Port, auf dem die Verbindung ankommt. Ein gefälschter Header `Host: …:8081` auf Port 8080 bekommt also auch 404.
- **Antwort:** Nur `Healthy`, `Degraded` oder `Unhealthy`, ohne Namen der Checks und ohne Fehlermeldungen.
- **Docker:** fragt `/health` alle 30 s ab, in der Startphase alle 2 s (`healthcheck` in `deploy/compose.yml`). Das geht per bash, weil das Image kein curl hat. Der Deploy wartet, bis alle Container healthy sind. Wird einer nicht healthy, scheitert der Deploy, und der vorherige Stand läuft wieder.
- **Worker:** haben kein HTTP und deshalb keinen Health-Check. Für sie gilt weiter, dass sie laufen und nicht neu gestartet sind.

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

Er braucht einen Eintrag in `AppHost.cs` und einen Dienst mit dem Anker `*app` in `deploy/compose.yml`, oder `*skeleton`, solange er im MVP nur Skelett ist. Ein Worker ohne HTTP bekommt dazu `healthcheck: disable: true`. Stimmen die beiden Listen nicht überein, schlägt der Job „Container images“ fehl.

## Secrets

- Die Container lesen Secrets nur aus `app.env` auf dem Server (`docs/secrets.md`). In `deploy/` steht kein Wert.
- SSH-Schlüssel, Ziel und Host-Schlüssel für den Deploy liegen als Secrets im Environment `production`. Das Environment darf nur `main` nutzen.
- Die Ausgabe des Deploys steht im öffentlichen Actions-Log. Deshalb gibt der Server keine Pfade, Hosts oder Benutzer aus.
