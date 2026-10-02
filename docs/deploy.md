# Edvaniq – Deploy

Ein Deploy bringt alle Prozesse eines Commits auf `main` gemeinsam auf den Server. Er startet nur von Hand, das ist die Freigabe. Automatisch rollt noch nichts aus. Derselbe Workflow kann auch auf den vorherigen Stand zurückgehen ([Rollback](#rollback)).

Auf dem Server läuft Edvaniq als eigene App einer gemeinsamen Plattform:
- eigener Linux-Benutzer
- eigener rootless Docker
- Obergrenze für Speicher und CPU

So kann Edvaniq keine andere App auf dem Server stören, und umgekehrt. Ausgerollt wird wie bei jeder App der Plattform: Der Workflow „Deploy“ ist eine Kopie der gemeinsamen Vorlage, die Deploy-Logik liegt auf dem Server.

Für Deploy und Rollback reicht GitHub, Server-Zugang braucht es dafür nicht. Was auf dem Server zu sehen und zu tun ist (Logs, Datenbank, Backups, Notfall), steht in [Betrieb](operations.md).

## Voraussetzungen

- Mitglied der Org `NULLRADIX-DEV` mit mindestens der Rolle „Write“ im Repo. Nur dann gibt es in Actions den Knopf „Run workflow“.
- Das Environment `production` erlaubt nur `main`. Ein Commit von einem anderen Branch lässt sich nicht ausrollen.

## Vor dem Deploy

1. Der PR ist auf `main` gemergt.
2. **Auf die CI warten.** Nach dem Merge läuft „CI“ auf `main` noch einmal, etwa 5 Minuten. Erst ihr letzter Schritt pusht die Images. Unter Actions → „CI“ muss der Lauf für den Merge-Commit grün sein, in seiner Zusammenfassung steht „Pushed 15 images“. Wer früher startet, bekommt „not all images … are available“. Passiert ist dann nichts, also einfach nach der CI noch einmal starten.
3. Neuer Service mit eigener Datenbank im Deploy? Dann muss seine DB auf dem Server schon angelegt sein ([Betrieb](operations.md#neue-service-datenbank)).

## Ausrollen

1. Actions → „Deploy“ → „Run workflow“
2. „environment“ bleibt `production`. Das Feld „commit“ leer lassen für den aktuellen Stand von `main`, oder einen älteren Commit von `main` eintragen (7 bis 40 Zeichen).
3. „rollback“ nicht anhaken.

Ein Lauf dauert 2–3 Minuten. Es läuft immer nur ein Deploy, ein zweiter wartet auf den ersten.

## Nach dem Deploy

- **Grün:** In der Zusammenfassung des Laufs steht „Deployed `<commit>`“ und das Rollback-Ziel, also der Stand davor.
- **Im Log des Schritts „Deploy“** stehen die Schritte in dieser Reihenfolge:

  ```
  Pulling images of <commit>
  Backed up the databases of db
  Migrating with planning-migrate
  planning-migrate finished
  Switching to <commit>
  Deployed <commit>
  Previous release: <vorheriger commit>
  ```

- **Rot:** Die letzte Zeile beginnt mit „Deploy failed:“. Was sie bedeutet, steht unter [Fehlermeldungen](#fehlermeldungen). Steht dort „nothing changed“ oder „the services were not switched“, läuft der alte Stand unverändert weiter.
- Wer Server-Zugang hat, sieht den Zustand der Container auch dort ([Betrieb](operations.md#zustand-ansehen)).

## Ablauf

Der Workflow prüft, dass der Commit auf `main` liegt, und schickt nur `deploy/compose.yml` aus genau diesem Commit per SSH an den Server. Der Deploy-Schlüssel darf dort nur den Empfang ausführen, und der kennt nur Deploy und Rollback. Danach läuft der Deploy der Plattform als App-Benutzer:

1. **Prüfen:**
   - Die Compose-Datei ist gültig und hält die Regeln der Plattform ein: keine Host-Mounts, kein Host-Netz, keine zusätzlichen Rechte, Images nur aus erlaubten Quellen, Ports nur lokal.
   - Alle Images des Commits liegen auf dem Server. Fehlende werden gezogen.
   - Jedes eigene Image trägt den Commit im Label `org.opencontainers.image.revision`.

   Scheitert hier etwas, bleibt der laufende Stand unverändert.
2. **Sichern:** Ein vollständiger Dump der laufenden Datenbank (Label `platform.backup: mysql` am Dienst `db`). Scheitert er, bleibt der laufende Stand unverändert.
3. **Migrieren:** Jeder Dienst im Profil `migrate` läuft einmal bis zum Ende, etwa `planning-migrate` ([Migrationen](#migrationen)). Scheitert einer, bricht der Deploy ab, und die Services laufen weiter auf dem alten Stand.
4. **Umschalten:** `docker compose up` für alle Dienste aus `deploy/compose.yml` außer den Migrationen. Das Image-Tag `sha-<commit>` setzt der Server über `APP_COMMIT`. Der Server wartet, bis jeder Container mit [Health-Check](#health) healthy ist.
5. **Prüfen:** Nach 20 s laufen alle Container. Keiner ist neu gestartet, und alle kommen aus dem Commit.

Scheitert Schritt 4 oder 5, startet der Server den vorherigen Stand wieder. Beim ersten Deploy gibt es keinen, dann fährt er alles herunter. So laufen nie alte und neue Prozesse gemischt.

Der Server behält die Releases und Images des aktuellen und des vorherigen Commits, ältere räumt der Deploy weg. Von den Dumps bleiben die 10 neuesten.

## Migrationen

Das Schema ändert sich nur in diesem Schritt, nie beim Start eines Service. Ein Migrationsschritt ist ein eigener Dienst in `deploy/compose.yml`, wie lokal die Ressource `<name>-migrate` im AppHost:

- **Gleiches Image wie die API,** mit dem Befehl `migrate`. Er gehört zum Profil `migrate`, deshalb startet `compose up` ihn nie. Dazu `restart: "no"` und kein Health-Check.
- **Vor dem Umschalten:** Der Server startet jeden Migrationsschritt nacheinander mit `docker compose run` und wartet auf das Ende. Läuft die Datenbank noch nicht, startet sie vorher.
- **Im Actions-Log** stehen nur „Migrating with …“ und das Ergebnis. Die Ausgabe selbst bleibt auf dem Server, weil sie Hosts und Benutzer nennen kann (`EdvaniqDoc/Betrieb.md`).
- **Scheitert eine Migration,** wird nicht umgeschaltet. Die Datenbank kann dann teilweise migriert sein, der Dump aus Schritt 2 ist von vorher.
- **Wiederholbar:** Jeder Deploy und jeder Rollback führt die Migrationen seines Stands aus. EF Core wendet nur an, was fehlt. Ein älteres Image stuft die Datenbank nie herunter.
- **Abwärtsverträglich:** Nach einem Fehlschlag oder Rollback läuft der alte Code gegen das neue Schema. Eine Spalte kommt also erst dazu, und die alte fällt erst in einem späteren Deploy weg.
- Die CI prüft, dass jedes `AddMigration` im AppHost seinen Migrationsschritt in `deploy/compose.yml` hat.

## Datenbank

- **Ein MySQL-Dienst `db`** (`mysql:9.7`, wie lokal und in den Tests) für alle Services. Jeder hat seine eigene Datenbank `<name>db` und einen eigenen Benutzer `<name>`, der nur auf sie darf. Die Daten liegen auf dem Server im Verzeichnis der App.
- **root** meldet sich nur im Container selbst an, für den Dump vor jedem Deploy. Aus dem Netz der App geht das nicht.
- **Port nur lokal auf dem Server:** Die Services erreichen die DB über den Dienstnamen `db`. Für die nächtlichen Backups und die Ansicht ist sie zusätzlich auf `127.0.0.1` des Servers veröffentlicht, nie öffentlich. Die Nummer steht nur auf dem Server (`DB_PORT`). Dort meldet sich ein Benutzer an, der nur lesen und sichern darf.
- **Datenbank eines neuen Service:** Auf dem Server legt ein Werkzeug der Plattform die DB und den Benutzer mit einem zufälligen Passwort an und schreibt den Connection String nach `<name>.env` (Befehl in `EdvaniqDoc/Betrieb.md`). Das muss vor dem ersten Deploy mit dem Service geschehen, sonst bricht der Deploy vor dem Umschalten ab.

## Speicher

Jeder Container hat eine eigene Speichergrenze und keinen Swap (`mem_limit` und `memswap_limit` in `deploy/compose.yml`):

| Prozesse | Grenze |
|---|---|
| alle, die im MVP produktiv werden (Identity, Planning, Content mit Worker, Knowledge, Assessment, Flashcards, LearningEngine, Gateway, Web) | 256 MB |
| die im MVP nur Skelett bleiben (Analytics, Notifications mit Worker, Gamification, Tutor), Anker `*skeleton` | 128 MB |

- **MySQL:** 1536 MB, davon 1 GB InnoDB-Cache (`--innodb-buffer-pool-size`). Der Rest bleibt für Verbindungen und Puffer.
- **Summe:** 10 × 256 + 5 × 128 + 1536 = 4736 MB. Das liegt unter der Obergrenze der App auf dem Server (6 GB ohne Swap). Die Reserve braucht Docker selbst und ein Migrationsschritt, solange er läuft.
- **Überschreitung:** Braucht ein Container mehr als seine Grenze, beendet der Kernel nur diesen Container, und Docker startet ihn neu (`restart: unless-stopped`). Die anderen Prozesse laufen weiter.
- **.NET kennt die Grenze:** Es begrenzt seinen Heap auf 75 % der Container-Grenze. Jeder Prozess schreibt beim Start `GC memory limit: <n> MiB` ins Log, bei 256 MB sind das 192 MiB.
- **Anheben:** Braucht ein Prozess mehr, wird seine Grenze im Dienst überschrieben. Die Summe muss unter der Obergrenze der App bleiben.

## Health

- **Wo:** Jeder Prozess mit HTTP meldet `/health` (bereit) und `/alive` (lebendig). Services aus der Vorlage sind erst bereit, wenn sie ihre eigene Datenbank erreichen ([Service-Vorlage](service-template.md#health)). Außerhalb von Development antworten beide nur auf dem internen Port 8081. Der wird nie veröffentlicht, also erreicht ihn nur der Container selbst und das Netz der App. Auf dem App-Port 8080, an den später der Proxy weiterleitet, antworten beide Pfade mit 404, bei Services mit Token-Prüfung ohne Token mit 401.
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

- Ein Rollback macht Datenmigrationen nicht rückgängig. Dafür gibt es den Dump vor jedem Deploy ([Restore](operations.md#restore)).
- Ist GitHub nicht erreichbar, gibt es einen [Notfallweg](operations.md#notfall-rollback-ohne-github) direkt auf dem Server.

## Fehlermeldungen

Die letzte Zeile eines roten Laufs nennt den Grund. Bei allem mit „nothing changed“ oder „not switched“ läuft der alte Stand unverändert weiter.

| Meldung | Bedeutung | Was tun |
|---|---|---|
| `Either a commit or rollback, not both` | „commit“ und „rollback“ sind beide gesetzt. | Eines davon leer lassen bzw. nicht anhaken. |
| `'…' is not a commit hash`, `Unknown commit …` | Im Feld „commit“ steht kein gültiger Commit. | 7 bis 40 Zeichen des Hashs eintragen, oder leer lassen. |
| `… is not on main` | Der Commit liegt auf keinem Stand von `main`. | Erst per PR nach `main` bringen. |
| `not all images of … are available, nothing changed` | Die Images des Commits sind noch nicht in der Registry. | Warten, bis „CI“ auf `main` für diesen Commit grün ist ([Vor dem Deploy](#vor-dem-deploy)), dann neu starten. |
| `… has revision …, nothing changed` | Ein Image gehört zu einem anderen Commit. | Nicht von Hand taggen. CI auf `main` für diesen Commit neu laufen lassen. |
| `compose.yml is invalid or an env file is missing, nothing changed` | `deploy/compose.yml` ist ungültig, oder auf dem Server fehlt eine Env-Datei, meist `<name>.env` eines neuen Service. | Neuer Service: zuerst seine [DB anlegen](operations.md#neue-service-datenbank). Sonst `deploy/compose.yml` lokal mit `docker compose config` prüfen. |
| `compose.yml breaks the platform rules, nothing changed` | Die Datei verletzt eine Regel der Plattform, etwa ein Port außerhalb des Bereichs, ein Host-Mount oder `cap_add`. | Änderung in `deploy/compose.yml` zurücknehmen. Die Regeln stehen unter [Ablauf](#ablauf), Schritt 1. |
| `backup before deploy failed, nothing changed` | Der Dump der DB ging nicht. | Zustand von `db` auf dem Server prüfen ([Betrieb](operations.md#zustand-ansehen)). |
| `<name>-migrate failed with exit code …`, danach `migration failed, the services were not switched` | Eine Migration ist gescheitert. Die DB kann teilweise migriert sein, die Services laufen auf dem alten Stand. | Ausgabe der Migration auf dem Server lesen ([Betrieb](operations.md#ausgabe-einer-migration)). Fix per PR, dann neu deployen. Notfalls den Dump von vorher einspielen ([Restore](operations.md#restore)). |
| `Switch to … failed`, danach `… is running again` | Ein Container wurde nicht healthy oder ist neu gestartet. Der vorherige Stand läuft wieder. | Logs des Containers lesen ([Betrieb](operations.md#logs)). |
| `restoring … failed too, check the containers` | Auch der vorherige Stand startet nicht. | Sofort auf dem Server nachsehen. Eher ein Problem der DB oder des Servers als des Codes. |
| `no previous release to restore, nothing is running` | Der erste Deploy ist gescheitert, einen vorherigen Stand gibt es nicht. | Ursache beheben und neu deployen. |
| `another deploy is running` | Ein anderer Deploy läuft gerade. | Warten, dann neu starten. |
| `Rollback failed: no previous release to roll back to, nothing changed` | Es gibt noch keinen vorherigen Stand. | Einen älteren Commit normal deployen. |

## Neuer Prozess

Er braucht einen Eintrag in `AppHost.cs` und einen Dienst mit dem Anker `*app` in `deploy/compose.yml`, oder `*skeleton`, solange er im MVP nur Skelett ist. Ein Worker ohne HTTP bekommt dazu `healthcheck: disable: true`. Stimmen die beiden Listen nicht überein, schlägt der Job „Container images“ fehl.

Über den Anker erbt jeder Prozess die Härtung: schreibgeschütztes Dateisystem bis auf `/tmp`, keine Capabilities, kein Rechtegewinn, eigene Grenzen für Prozesse, Speicher und CPU. Der Server lehnt einen Dienst ohne diese Härtung vor dem Umschalten ab.

Ein Service aus der Vorlage hat eine eigene Datenbank. Seine Prozesse bekommen deshalb einen eigenen Anker wie `x-planning`, der zusätzlich `<name>.env` liest, im Netz `backend` der DB hängt und auf `db` wartet. Die übrigen Prozesse erreichen die DB nicht. Dazu kommt der Migrationsschritt `<name>-migrate`. Vorbild ist Planning, die Schritte stehen in der [Service-Vorlage](service-template.md#gerüst-ersetzen).

## Secrets

- Die Container lesen Secrets nur aus Dateien auf dem Server (`docs/secrets.md`): `app.env` für alle, `<name>.env` mit dem Connection String nur für die Prozesse des Service. In `deploy/` steht kein Wert.
- SSH-Schlüssel, Ziel und Host-Schlüssel für den Deploy liegen als Secrets im Environment `production`. Das Environment darf nur `main` nutzen.
- Die Ausgabe des Deploys steht im öffentlichen Actions-Log. Deshalb gibt der Server keine Pfade, Hosts oder Benutzer aus.
