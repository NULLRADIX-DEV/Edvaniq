# Einen neuen Service anlegen

Ein neuer Service entsteht nur aus der Vorlage in `templates/service/`, nie von Hand. So sind alle Services gleich gebaut, und keiner vergisst den Schutz der Daten.

Du brauchst das, wenn ein Fachbereich einen eigenen Service bekommt. Für einen der vorhandenen Gerüst-Services gilt derselbe Weg mit den Unterschieden unten. Was die Vorlage mitbringt und warum, steht im [Aufbau eines Service](service-internals.md).

## Voraussetzungen

Du brauchst einen Klon des Repos, in dem einmal `dotnet tool restore` gelaufen ist, das bringt `dotnet ef` mit. Docker Desktop muss laufen, sonst scheitern die Integrationstests und der AppHost wartet ewig auf die Datenbank. Ohne Docker baust du lokal und lässt nur die Unit- und Architekturtests laufen. Integrationstests, Image und Health zeigt dir dann erst die CI, stell den Pull Request deshalb früh als Entwurf. Bitte früh jemanden mit Zugang zum Server, die Datenbank des Service anzulegen, denn ohne sie darf der Pull Request nicht nach `main`.

Den Namen schreibst du in PascalCase, etwa `LearningEngine`. Daraus werden die Projekte `Edvaniq.Services.LearningEngine.*`, die Datenbank `learningenginedb` und der Name `learningengine-api` in AppHost und Compose. Unten steht `<Name>` für die PascalCase-Form und `<name>` für die kleingeschriebene. Die Vorlage prüft die Schreibweise nicht: Aus `-n learningengine` entstehen Klassen wie `learningengineDbContext`.

## Schritte

1. Vorlage installieren und den Service erzeugen, beides im Repo-Root:

   ```
   dotnet new install ./templates/service --force
   dotnet new edvaniq-service -n <Name>
   ```

   Das legt fünf Projekte unter `src/Services/<Name>/` und zwei Testprojekte unter `tests/Services/<Name>/` an und trägt alle sieben in `Edvaniq.slnx` ein. Mit `--dry-run` am zweiten Befehl siehst du vorher, was entstehen würde. Was in den Dateien steckt, zeigt [Was entsteht](service-internals.md#was-entsteht).

2. In `tests/Edvaniq.ArchitectureTests/Edvaniq.ArchitectureTests.csproj` die fünf Projekte unter `src/Services/<Name>/` referenzieren. Sonst wird `Rule5ProjectionTests` rot, weil er den DbContext des Service nicht laden kann.

3. Das Beispiel `ExampleItem` durch die erste echte Entity ersetzen ([Beispiel](service-internals.md#beispiel)):

   1. Die eigene Entity nach demselben Muster anlegen, mit `IOwnedByUser`, wenn sie einem Nutzer gehört.
   2. `ExampleItem`, `IExampleItems`, `ExampleItems`, `ExampleEndpoints` und die beiden Zeilen dafür (`AddScoped`, `MapExampleEndpoints`) löschen, dazu in `<Name>DbContext` das `DbSet` und die Zeile in `ConfigureModel`.
   3. `ExampleItemCreated`, `ExampleItemCreatedHandler` und die Zeile `AddIntegrationEventHandler<ExampleItemCreated, ExampleItemCreatedHandler>` löschen. `AddMessaging`, die Tabellen für Outbox und Inbox und der `UserDeletedHandler` mit seinen beiden Zeilen bleiben.
   4. Eine Migration anlegen. Sie entfernt die Tabelle `ExampleItems` und legt die eigene an:

      ```
      dotnet ef migrations add <Migration> --project src/Services/<Name>/Edvaniq.Services.<Name>.Infrastructure
      ```

   5. Die Tests umstellen. `IsolationTests` prüft die eigenen Endpunkte, der Test „Nutzer A sieht B nicht“ bleibt Pflicht. `UserDeletionTests` legt statt der Items die eigenen Daten an. `ProblemTests` fragt einen eigenen Endpunkt mit einer unbekannten ID ab, statt `/examples/{id}`. `MessagingTests` zeigt auf das erste eigene Event. Sendet der Service noch keins, löschst du `MessagingTests` und legst ihn mit dem ersten Event wieder an, denn den Baustein selbst prüfen die Tests in `tests/BuildingBlocks`.

4. Die Tests laufen lassen. Grün heißt, Token-Prüfung, Datenbank, Nachrichten und das Löschen eines Nutzers funktionieren auch mit der eigenen Entity:

   ```
   dotnet test --project tests/Services/<Name>/Edvaniq.Services.<Name>.IntegrationTests
   ```

   Ohne Docker laufen sie erst in der CI des Pull Request.

5. Den Service im AppHost eintragen. Die Api kommt als `ProjectReference` in `Edvaniq.AppHost.csproj`. In `AppHost.cs` kommt jede der drei Zeilen in ihren Block, also zu den Datenbanken, den Migrationen und den Services, und das Gateway bekommt `WithReference(<name>Api)`:

   ```csharp
   var <name>Db = mysql.AddServiceDatabase("<name>");
   var <name>Migrate = builder.AddMigration<Projects.Edvaniq_Services_<Name>_Api>("<name>-migrate", <name>Db);
   var <name>Api = AddService<Projects.Edvaniq_Services_<Name>_Api>("<name>-api", <name>Db)
       .WaitForCompletion(<name>Migrate)
       .WithMessaging(nats);
   ```

   Die Variablen schreibst du in camelCase wie die übrigen im AppHost, für LearningEngine also `learningEngineDb`. `WithMessaging(nats)` gibt dem Service die Adresse von NATS. Ein Service, der nichts sendet oder empfängt, lässt es weg.

6. Lokal prüfen. Starte den AppHost mit `dotnet run --project src/Aspire/Edvaniq.AppHost`. Den Port des Service zeigt das Aspire-Dashboard, er steht auch im Profil `http` von `Properties/launchSettings.json` der Api. `curl -i http://localhost:<port>/health` antwortet mit 200 `Healthy`, sobald der Service seine Datenbank erreicht. Mit einem [Dev-Token](service-internals.md#token-prüfung) antwortet `curl -i -H "Authorization: Bearer <token>" http://localhost:<port>/me` mit dem Nutzer aus dem Token. Ohne Docker entfällt dieser Schritt, dasselbe prüfen `HealthTests` und `TokenTests` in der CI.

7. Den Service in `deploy/compose.yml` eintragen, mit einem Anker `x-<name>`, darauf dem Dienst `<name>-api` und dem Migrationsschritt `<name>-migrate`. Vorbild ist Planning:

   ```yaml
   x-<name>: &<name>
     <<: *app
     env_file:
       - path: app.env
         required: true
       - path: <name>.env
         required: true
     networks: [inner, data]
     depends_on:
       db:
         condition: service_healthy

   services:
     <name>-api:
       <<: *<name>
       image: ghcr.io/nullradix-dev/edvaniq/<name>-api:sha-${APP_COMMIT:?}
     <name>-migrate:
       <<: *<name>
       image: ghcr.io/nullradix-dev/edvaniq/<name>-api:sha-${APP_COMMIT:?}
       command: ["migrate"]
       profiles: [migrate]
       restart: "no"
       healthcheck:
         disable: true
   ```

   Weil `env_file` und `networks` die Listen aus `x-app` ersetzen, stehen `app.env` und `inner` darin noch einmal. Die Datenbank erreichen nur Prozesse im Netz `data`, also nur die mit eigener Datenbank. Das Netz `backend` gehört der Datenbank allein, für ihren Port ([Erreichbarkeit](deploy.md#erreichbarkeit)). Den Aussteller der Tokens und die `Authority` setzt `x-app` für alle.

8. Soll ein Client den Service erreichen, braucht das Gateway eine Route dorthin. Leg in seiner `appsettings.json` unter `ReverseProxy` eine Route `/<name>/{**rest}` mit dem Transform `PathRemovePrefix: /<name>` an, dazu einen Cluster mit dem Ziel `https+http://<name>-api` und einem `HttpRequest:ActivityTimeout`. Vorbild ist Planning. Auf dem Server findet das Gateway den Service über die Zeile `services__<name>-api__http__0: http://<name>-api:8080` im Abschnitt `environment` des Dienstes `gateway` in `deploy/compose.yml` ([Gateway](architecture.md#gateway)). Für den Browser braucht zusätzlich der Web-Host eine Route in seinem Abschnitt `ReverseProxy` ([Clients](architecture.md#clients)).

9. Den Pull Request stellen. AppHost, `deploy/compose.yml` und die Routen gehören in denselben Pull Request, denn die CI prüft, dass AppHost und Compose dieselben Prozesse und Migrationsschritte haben. Merge ihn erst, wenn die Datenbank auf dem Server angelegt ist, sonst bricht der nächste Deploy vor dem Umschalten ab.

## Woran du merkst, dass es geklappt hat

Die CI ist grün, einschließlich der Architekturtests und des Abgleichs von AppHost und `deploy/compose.yml`. Lokal meldet `/health` den Service als `Healthy`, und `/me` nennt den Nutzer aus dem Token. Nach dem nächsten Deploy ist der Workflow „Deploy“ grün, und damit sind `<name>-migrate` und `<name>-api` auf dem Server gelaufen.

## Einen Gerüst-Service ersetzen

Die meisten Services stammen noch aus dem ersten Skelett: fünf Projekte ohne Inhalt, ohne Datenbank und ohne Tests. Planning ist so aus der Vorlage neu entstanden (#125). Identity gehört nicht dazu, es stellt schon die Tokens aus und wird nicht neu erzeugt. Für die übrigen ist der Weg derselbe wie oben, mit vier Unterschieden:

1. Vor Schritt 1 die Projekte des Service aus `Edvaniq.slnx` austragen (`dotnet sln Edvaniq.slnx remove …`) und ihre Ordner unter `src/Services/<Name>/` und `tests/Services/<Name>/` ganz löschen, auch `bin/` und `obj/`. Sonst bricht `dotnet new` ab, weil es Dateien überschreiben müsste. Bei Content und Notifications bleibt der Worker stehen, samt seinem Eintrag in der Solution, denn die Vorlage erzeugt keinen.
2. Schritt 2 entfällt. Die Pfade bleiben gleich, die Verweise anderer Services auf `.Contracts` und die Referenzen in `Edvaniq.ArchitectureTests` stimmen also weiter.
3. Im AppHost stehen `AddServiceDatabase` und `AddService` schon da, es fehlen nur der Migrationsschritt, `WaitForCompletion` und `WithMessaging(nats)`.
4. In `deploy/compose.yml` stellst du den vorhandenen Dienst `<name>-api` auf den eigenen Anker um und ergänzt `<name>-migrate` (#126).

## Wenn es nicht klappt

- Heißt der neue Service `Edvaniq.Services.Edvaniq.*`, fehlte `-n`, und die Vorlage hat den Namen des aktuellen Ordners genommen. Außerhalb des Repos findet sie keine `Edvaniq.slnx` und endet mit Exit-Code 105, in einem Unterordner wie `src/` landet alles eine Ebene zu tief.
- Heißen die Klassen etwa `learningengineDbContext`, stand der Name nicht in PascalCase. Erzeug den Service neu.
- Wird `Ports_AreUniqueAcrossProcesses` in `Edvaniq.ArchitectureTests` rot, hat die Vorlage einen Port gezogen, den schon ein anderer Prozess hat. Lösch den neuen Service und erzeug ihn neu, solange du noch nichts an ihm geändert hast. Sonst setz den Port in seiner `launchSettings.json` auf einen freien.
- Zum Neu-Erzeugen trägst du die sieben Projekte mit `dotnet sln Edvaniq.slnx remove …` aus, löschst `src/Services/<Name>/` und `tests/Services/<Name>/` und rufst `dotnet new edvaniq-service -n <Name>` noch einmal auf. Prüf vorher, dass beide Ordner wirklich weg sind. Hält die IDE oder ein laufender Build noch Dateien darin, bleibt der Ordner stehen, und Windows behält beim neuen Erzeugen seine alte Schreibweise. Lokal baut dann alles, aber in der CI auf Linux passen die Pfade in `Edvaniq.slnx` nicht mehr zu den Ordnern.
- Meldet der CI-Job „Container images“ „deploy/compose.yml does not match the processes in AppHost.cs“ oder „The migration steps in deploy/compose.yml do not match AddMigration in AppHost.cs“, steht ein Prozess oder Migrationsschritt nur in einer der beiden Dateien. Darüber listet der Lauf mit `<`, was in Compose fehlt, und mit `>`, was dort zu viel ist.

Fehler, die erst ein laufender Service zeigt, stehen bei den [Stolperfallen](service-internals.md#stolperfallen).