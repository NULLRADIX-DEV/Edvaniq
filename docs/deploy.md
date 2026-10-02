# Deploy

Ein Deploy bringt alle Prozesse eines Commits auf `main` gemeinsam auf den Server. Er startet nur von Hand, und genau das ist die Freigabe. Was niemand bewusst ausrollt, geht nicht live. Für Deploy und Rollback reicht GitHub, Zugang zum Server brauchst du dafür nicht.

## Voraussetzungen

Du brauchst in der Org `NULLRADIX-DEV` mindestens die Rolle „Write“ im Repo, sonst fehlt in Actions der Knopf „Run workflow“. Ausrollen lässt sich nur ein Commit, der auf `main` liegt. Das prüft der Workflow selbst, und das Environment `production` gibt seine Schlüssel nur an Läufe von `main` heraus.

## Einen Commit ausrollen

1. Den Pull Request auf `main` mergen.
2. Auf die CI warten. Nach dem Merge läuft „CI“ auf `main` noch einmal, etwa 5 Minuten, und erst ihr letzter Schritt lädt die Images hoch. Der Lauf für den Merge-Commit muss grün sein, in seiner Zusammenfassung steht dann „Pushed 15 images“.
3. Actions → „Deploy“ → „Run workflow“. „environment“ bleibt `production`. „commit“ bleibt leer für den aktuellen Stand von `main`, für einen älteren Commit von `main` trägst du 7 bis 40 Zeichen seines Hashs ein. „rollback“ bleibt aus.

Ein Lauf dauert 2 bis 3 Minuten. Es läuft immer nur ein Deploy, ein zweiter wartet auf den ersten.

Bringt der Commit einen neuen Service mit eigener Datenbank mit, muss diese Datenbank auf dem Server schon angelegt sein. Das erledigt vorher jemand mit Server-Zugang, sonst bricht der Deploy vor dem Umschalten ab.

## Was dabei passiert

Der Workflow schickt nur `deploy/compose.yml` aus genau diesem Commit an den Server. Dort läuft Edvaniq getrennt von anderen Apps und mit festen Grenzen für Speicher und CPU. So kann Edvaniq keine andere App stören, und umgekehrt.

Der Server prüft die Compose-Datei und holt die Images des Commits. Danach sichert er die Datenbank, führt die Migrationen aus und schaltet erst dann alle Dienste um. Er wartet, bis jeder Container healthy ist. Scheitert etwas vor dem Umschalten, läuft der alte Stand unverändert weiter. Scheitert das Umschalten selbst, startet der Server den vorherigen Stand wieder, alte und neue Prozesse laufen also nie gemischt. Nur in zwei Fällen gibt es keinen vorherigen Stand. Scheitert der allererste Deploy, fährt der Server alles herunter. Scheitert ein erneuter Deploy des Commits, der gerade läuft, muss jemand auf dem Server nachsehen.

## Hat es geklappt?

Ist der Lauf grün, steht in seiner Zusammenfassung „Deployed `<commit>`“ und darunter der Stand davor, zu dem ein Rollback zurückginge.

Ist er rot, nennt die letzte Zeile im Log den Grund. Endet sie mit „nothing changed“ oder „the services were not switched“, läuft der alte Stand weiter. Die Meldungen, die du selbst beheben kannst:

| Meldung | Was tun |
|---|---|
| `not all images of … are available` | Zu früh gestartet. Warten, bis „CI“ auf `main` für diesen Commit grün ist, dann neu starten. |
| `… has revision …` | Ein Image gehört zu einem anderen Commit. Nie von Hand taggen, sondern „CI“ auf `main` für diesen Commit neu laufen lassen. |
| `'…' is not a commit hash`, `Unknown commit …`, `… is not on main` | Im Feld „commit“ steht kein Commit von `main`. Leer lassen oder den richtigen Hash eintragen. |
| `… has no deploy/compose.yml` | Der Commit ist älter als der Deploy, er lässt sich nicht ausrollen. |
| `Either a commit or rollback, not both` | Nur eines von beiden setzen. |
| `compose.yml breaks the platform rules` | Ein Dienst in `deploy/compose.yml` hält die Regeln nicht ein, meist fehlt die Härtung aus dem Anker ([Neuer Prozess](#neuer-prozess)) oder ein Port liegt außerhalb des Bereichs der App. Die Änderung zurücknehmen. |
| `compose.yml is invalid or an env file is missing` | Lokal mit `docker compose config` prüfen. Bei einem neuen Service fehlt meist seine Datenbank auf dem Server. |
| `<name>-migrate failed …` | Die Migration ist gescheitert, die Services laufen auf dem alten Stand. Fix per Pull Request, dann neu deployen. |
| `Rollback failed: no previous release …` | Es gibt noch keinen vorherigen Stand. Einen älteren Commit normal deployen. |
| `another deploy is running` | Warten und neu starten. |

Alle anderen Meldungen deuten auf den Server hin, dann hilft jemand mit Server-Zugang. Das Actions-Log ist öffentlich, deshalb nennt der Server dort keine Pfade, Hosts oder Benutzer.

## Rollback

Den brauchst du, wenn der neue Stand zwar läuft, aber fachlich kaputt ist. Scheitert schon der Deploy, ist kein Rollback nötig, denn dann läuft der vorherige Stand ohnehin wieder.

1. Actions → „Deploy“ → „Run workflow“
2. „rollback“ anhaken und „commit“ leer lassen.

Nach 1 bis 2 Minuten ist der Lauf grün, und die Zusammenfassung nennt den Commit, der jetzt läuft. Der Rollback durchläuft dieselben Prüfungen wie ein Deploy, und scheitert er, läuft der aktuelle Stand weiter. Die Images des vorherigen Stands liegen noch auf dem Server, er braucht also die Registry nicht.

Danach ist der kaputte Stand der „vorherige“. Ein zweiter Rollback ginge also wieder nach vorn, und ein Deploy ohne Commit rollt `main` aus, also wieder den Fehler. Deploye deshalb erst, wenn der Fix auf `main` ist. Weiter zurück als einen Stand geht es mit einem normalen Deploy und dem alten Commit im Feld „commit“. Dessen Images holt der Server dann wieder aus der Registry.

Ein Rollback macht Migrationen nicht rückgängig. Dafür gibt es die Sicherung vor jedem Deploy, die jemand mit Server-Zugang einspielen kann.

## Migrationen

Das Schema ändert sich nur im Deploy, nie beim Start eines Service. Ein Migrationsschritt ist ein eigener Dienst in `deploy/compose.yml`, so wie lokal die Ressource `<name>-migrate` im AppHost. Er nutzt das Image der API mit dem Befehl `migrate` und gehört zum Profil `migrate`, deshalb startet ihn `compose up` nie. Dazu kommen `restart: "no"` und kein Health-Check. Die CI prüft, dass jedes `AddMigration` im AppHost so einen Dienst hat.

Aus dem Ablauf oben folgen zwei Regeln. Migrationen müssen sich wiederholen lassen, denn jeder Deploy und jeder Rollback führt die Migrationen seines Stands aus. EF Core wendet dabei nur an, was fehlt, und ein älteres Image stuft die Datenbank nie herunter. Außerdem müssen Migrationen abwärtsverträglich sein, weil nach einem Fehlschlag oder Rollback der alte Code gegen das neue Schema läuft. Eine neue Spalte kommt also zuerst dazu, und die alte fällt erst in einem späteren Deploy weg.

Scheitert eine Migration, wird nicht umgeschaltet, und die Datenbank kann halb migriert sein ([warum](service-template.md#datenbank-und-migrationen)). Im Actions-Log steht nur, dass die Migration gescheitert ist. Ihre Ausgabe bleibt auf dem Server, weil sie Hosts und Benutzer nennen kann.

## Neuer Prozess

Jeder Prozess braucht einen Eintrag in `AppHost.cs` und einen Dienst in `deploy/compose.yml`. Stimmen die beiden Listen nicht überein, schlägt der CI-Job „Container images“ fehl.

Über den Anker `*app` erbt der Dienst die Härtung: ein schreibgeschütztes Dateisystem bis auf `/tmp`, keine Capabilities, keinen Rechtegewinn und eigene Grenzen für Prozesse, Speicher und CPU. Fehlen die Grenzen, `cap_drop: [ALL]` oder `no-new-privileges`, lehnt der Server die Datei ab. Solange ein Prozess im MVP nur Skelett ist, nimmt er `*skeleton`. Ein Worker ohne HTTP bekommt dazu `healthcheck: disable: true`.

Alle Services teilen sich den MySQL-Dienst `db`, jeder mit eigener Datenbank und eigenem Benutzer. Ein Service mit Datenbank bekommt deshalb einen eigenen Anker wie `x-planning`, nur er erreicht die Datenbank. Wie das aussieht, zeigt die [Service-Vorlage](service-template.md#danach-von-hand).

## Speicher

Jeder Container hat eine feste Speichergrenze ohne Swap (`mem_limit` und `memswap_limit`). Produktive Prozesse haben 256 MB, die Skelett-Prozesse 128 MB und MySQL 1536 MB, davon 1 GB InnoDB-Cache. Zusammen sind das 4736 MB, und das liegt unter der Obergrenze von 6 GB für die ganze App. Den Rest brauchen Docker selbst und ein laufender Migrationsschritt.

Braucht ein Container mehr als seine Grenze, beendet der Kernel nur ihn, und Docker startet ihn neu. .NET kennt die Grenze und hält seinen Heap bei 75 % davon. Jeder Prozess schreibt beim Start `GC memory limit: <n> MiB` ins Log, bei 256 MB sind das 192 MiB. Braucht ein Prozess dauerhaft mehr, überschreibst du seine Grenze im Dienst. Die Summe muss dann weiter unter 6 GB bleiben.

## Health

Jeder Prozess mit HTTP meldet `/alive` (der Prozess lebt) und `/health` (er ist bereit). Services aus der Vorlage sind erst bereit, wenn sie ihre eigene Datenbank erreichen ([Service-Vorlage](service-template.md#health)). Außerhalb von Development antworten beide nur auf dem internen Port 8081, den nur der Container selbst und das Netz der App erreichen. Auf dem App-Port 8080 gibt es für beide Pfade 404, bei Services mit Token-Prüfung ohne Token 401. Das gilt auch mit einem gefälschten Header `Host: …:8081`, denn es zählt der Port, auf dem die Verbindung ankommt. Die Antwort ist nur `Healthy`, `Degraded` oder `Unhealthy`, ohne Namen der Checks und ohne Fehlermeldungen.

Docker fragt `/health` alle 30 Sekunden ab, beim Start alle 2 Sekunden. Das läuft per bash, weil die Images kein curl haben. Der Deploy wartet, bis alle Container healthy sind, und wird einer es nicht, läuft wieder der vorherige Stand. Worker haben kein HTTP und deshalb keinen Health-Check. Für sie zählt, dass sie laufen und nicht neu starten.
