# Edvaniq – Betrieb

Für Entwickler, die nachvollziehen wollen, was auf dem Server läuft, und im Notfall eingreifen müssen. Deploy und Rollback gehen ohne Server-Zugang über GitHub ([Deploy](deploy.md)).

Das Repo ist öffentlich. Deshalb stehen hier Platzhalter, die konkreten Werte nur im privaten `EdvaniqDoc/Betrieb.md` (Abschnitt „Werte für docs/operations.md“):

| Platzhalter | Bedeutung |
|---|---|
| `<server>` | der VPS, per SSH |
| `<tailnet>` | Adresse des Servers im Tailnet, für die Oberflächen |
| `<app>` | Name von Edvaniq auf der Server-Plattform. Zugleich Linux-Benutzer, Compose-Projekt und Anfang der Containernamen (`<app>-planning-api-1`). |
| `<app-dir>` | Verzeichnis der App auf dem Server |
| `<kit>` | Verzeichnis mit den Skripten der Plattform |

## Zugang

| Zugang | Wofür | Wie |
|---|---|---|
| GitHub, Rolle „Write“ | Deploy, Rollback, Logs der Läufe | Einladung in die Org `NULLRADIX-DEV` durch den Admin |
| Tailnet (Tailscale) | Portainer, phpMyAdmin, 1Panel | Einladung ins Tailnet durch den Admin. Die Oberflächen sind nur von dort erreichbar, nie über das Internet. |
| SSH als root auf `<server>` | Zustand und Logs von Hand, Notfall-Rollback, DB-Benutzer, Restore | Eigenen Schlüssel erzeugen, den öffentlichen Teil dem Admin geben, der trägt ihn ein. Nur mit Schlüssel, ohne Passwort. |
| 1Panel | Überwachung des Hosts, nächtliche Backups | Konto mit 2FA durch den Admin |

```
ssh-keygen -t ed25519 -C "<dein-name>"     # danach ~/.ssh/id_ed25519.pub an den Admin
ssh root@<server>
```

- Mehrere Fehlversuche bei SSH sperren die eigene IP eine Zeit lang. Über das Tailnet kommt man trotzdem immer auf den Server.
- Die Apps selbst haben keine Shell. Ihr Deploy-Schlüssel darf nur Deploy und Rollback auslösen. Alle Arbeiten von Hand laufen als root über das Werkzeug `platform`.
- Was der Admin für einen neuen Entwickler einrichtet, steht in `EdvaniqDoc/Betrieb.md`.

## Zustand ansehen

- **Portainer** (im Tailnet): Umgebung `<app>`. Dort sieht man Container, Status, Logs und Speicher und kann eine Shell im Container öffnen. Stacks dort nie bearbeiten, die Quelle ist `deploy/compose.yml` im Repo.
- **Per SSH als root:**

  ```
  platform list                               # alle Apps: Docker-Zustand, Container, Speicher/Obergrenze
  platform docker <app> ps                    # Container mit Status, HTTP-Dienste zeigen (healthy)
  platform docker <app> stats --no-stream     # Speicher je Container
  ```

- **Health eines Containers** von innen, denn Port 8081 ist nicht veröffentlicht ([Health](deploy.md#health)):

  ```
  platform docker <app> exec <app>-planning-api-1 bash -c 'exec 3<>/dev/tcp/127.0.0.1/8081; printf "GET /health HTTP/1.0\r\n\r\n" >&3; cat <&3'
  ```

- **Welcher Stand läuft:** `cat <app-dir>/current`. Der Stand davor steht in `<app-dir>/previous`, er ist das Ziel eines Rollbacks.

## Logs

```
platform docker <app> logs --since 30m <app>-planning-api-1
platform docker <app> logs --tail 200 -f <app>-gateway-1
```

- Docker hält je Container nur eine begrenzte Menge Log und löscht das Älteste.
- In den Logs stehen keine Secrets und keine Connection Strings ([Secrets](secrets.md)). Wer ein Log weitergibt, prüft das trotzdem vorher.

## Ausgabe einer Migration

Im Actions-Log steht nur, ob eine Migration geklappt hat. Ihre Ausgabe kann Hosts und Benutzer nennen, deshalb bleibt sie auf dem Server:

```
cat <app-dir>/releases/<commit>/planning-migrate.log
```

Der Server behält die Releases des aktuellen und des vorherigen Stands, ältere räumt der nächste Deploy weg.

## Datenbank

- **Eine MySQL für alle Services**, der Dienst `db` (`<app>-db-1`). Jeder Service hat seine DB `<name>db` und einen eigenen Benutzer `<name>`, der nur darauf darf. Den Connection String hat nur der Service, in `<app-dir>/<name>.env`.
- **Ansehen:** phpMyAdmin im Tailnet, Server `<app>`, mit dem Benutzer, der nur lesen darf. Name und Passwort hat der Admin.
- **Abfrage als Service-Benutzer** per SSH, hier für Planning:

  ```
  pw=$(sed -n 's/.*;Password=//p' <app-dir>/planning.env)
  platform docker <app> run --rm --network <app>_default -e MYSQL_PWD="$pw" mysql:9.7 mysql -hdb -uplanning planningdb
  ```

  Das Passwort nie ausgeben, auch nicht in einem Skript, das seine Befehle mitschreibt (`set -x`, `echo "$@"`). Ist es doch einmal sichtbar geworden: `<app-dir>/<name>.env` als App-Benutzer leeren (`runuser -u <app> -- sh -c ': > <app-dir>/<name>.env'`), `platform db-user <app> <name>` erzeugt dann ein neues, und ein Deploy startet die Container mit dem neuen Passwort.

- **Schema ändern** nur per Migration im Repo, nie von Hand ([Migrationen](deploy.md#migrationen)).

## Backups

| Sicherung | Wann | Wo | Behalten |
|---|---|---|---|
| Dump aller Datenbanken | vor jedem Deploy, vor den Migrationen | `<app-dir>/backups/` | die 10 neuesten |
| Dump je Datenbank (1Panel) | jede Nacht | Backup-Verzeichnis von 1Panel, nur root | 30 |

Beide liegen auf demselben Server. Ein Backup an einem anderen Ort kommt erst später (M1/M5).

## Restore

Nur bewusst und nie während eines Deploys. Ein Restore überschreibt die Daten seit dem Dump. Geprobt ist der Weg für MySQL noch nicht, das kommt mit #166.

1. Den passenden Dump wählen:
   - Vor einer kaputten Migration: der Dump aus `<app-dir>/backups/` mit der Uhrzeit des Deploys (`db-<datum>-<uhrzeit>.sql.gz`).
   - Sonst: das nächtliche Backup aus 1Panel.
2. Zur Sicherheit vorher den jetzigen Stand sichern:

   ```
   platform docker <app> exec <app>-db-1 sh -c 'MYSQL_PWD=$MYSQL_ROOT_PASSWORD exec mysqldump -uroot --all-databases --single-transaction --routines --events --triggers' | gzip > /root/vor-restore.sql.gz
   ```

3. Einspielen. Ein Dump vor dem Deploy enthält alle DBs samt Benutzern:

   ```
   zcat <app-dir>/backups/<datei>.sql.gz | platform docker <app> exec -i <app>-db-1 sh -c 'MYSQL_PWD=$MYSQL_ROOT_PASSWORD exec mysql -uroot'
   ```

   Ein Dump aus 1Panel enthält nur eine DB, dann kommt ihr Name ans Ende: `… exec mysql -uroot planningdb`.
4. Die betroffenen Services neu starten, damit sie keine alten Verbindungen halten: `platform docker <app> restart <app>-planning-api-1`.

## Notfall-Rollback ohne GitHub

Wenn GitHub nicht erreichbar ist. Es ist derselbe Weg wie das Häkchen „rollback“ im Workflow, mit denselben Prüfungen, und er braucht die Registry nicht:

```
runuser -u <app> -- env SSH_ORIGINAL_COMMAND=rollback <kit>/receive
```

Danach weiß GitHub nichts davon. Die Zusammenfassung des letzten Laufs zeigt also einen anderen Stand als den, der läuft. Was wirklich läuft, steht in `<app-dir>/current`.

## Neue Service-Datenbank

Bevor ein Service mit eigener DB zum ersten Mal deployt wird ([Service-Vorlage](service-template.md#gerüst-ersetzen)):

1. **Als root anlegen:**

   ```
   platform db-user <app> <name>
   ```

   Das legt die DB `<name>db` und den Benutzer `<name>` mit einem zufälligen Passwort an, nur aus dem Netz der App erreichbar. Den Connection String schreibt es nach `<app-dir>/<name>.env`. Ein zweiter Aufruf behält das Passwort.
2. **In 1Panel:** beim Eintrag der Edvaniq-DB „Vom Server synchronisieren“, dann einen Cronjob „Backup database“ für die neue DB anlegen.
3. **Erst dann** den PR mit dem Anker `x-<name>`, `<name>-api` und `<name>-migrate` mergen und deployen.

## Neustart und Updates

- Der Server startet regelmäßig neu, und wenn ein Update es verlangt (Zeitplan in `EdvaniqDoc/Betrieb.md`). Danach kommt alles von selbst wieder hoch. Startet der Docker einer App neu, starten auch ihre Container neu.
- Nach einem Update von Docker auf dem Host laufen die Docker der Apps noch mit dem alten Programm, bis `platform restart-daemons` sie neu startet. Ihre Container starten dabei kurz neu.

## Nie

- In 1Panel bei einer Datenbank auf „Delete“ klicken. Das löscht die echte Datenbank, nicht nur den Eintrag.
- Den Admin-Benutzer der Datenbanken in 1Panel eintragen. Dort steht nur der Benutzer, der lesen darf.
- Stacks in Portainer bearbeiten, Container von Hand starten oder Images von Hand taggen. Der nächste Deploy überschreibt das, die Quelle ist das Repo.
- Einen Port anders als auf `127.0.0.1` veröffentlichen oder in der Firewall von 1Panel öffnen.
- Server-Details wie IPs, Benutzer oder Pfade ins Repo schreiben. Es ist öffentlich, sie gehören nach `EdvaniqDoc/Betrieb.md`.
