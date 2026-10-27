# Edvaniq
Learn smarter. Master anything.

## Lokal starten

Ein Befehl startet das ganze Backend und die Web-Shell: MySQL und den Message Broker NATS als Container, alle Services und Worker, das Gateway und die Web-App. Dazu öffnet sich das Aspire-Dashboard, in dem du jeden Prozess mit Logs und Traces siehst.

Du brauchst einen laufenden Docker Desktop und das .NET SDK in der Version aus `global.json`. Die MAUI-Workload brauchst du nur, um `Edvaniq.App` zu bauen. Der AppHost kommt ohne sie aus.

```
dotnet run --project src/Aspire/Edvaniq.AppHost
```

Beim ersten Mal dauert das länger, weil Docker die Images für MySQL, phpMyAdmin und NATS lädt und .NET die Aspire-CLI holt. Zugangsdaten legst du nicht an. Der AppHost erzeugt die Passwörter der Datenbank-Benutzer, den Signierschlüssel von Identity und das Client-Secret des Web-Hosts beim ersten Start selbst und legt sie in seinen User Secrets ab ([docs/secrets.md](docs/secrets.md#lokal)).

Ob alles läuft, zeigt die Web-App. Öffne sie im Dashboard über den Link in der Zeile `web` und geh auf die Seite `/systemstatus`. Dort läuft ein Ping über die Web-App und das Gateway durch Planning bis in dessen Datenbank und kommt mit dem zurück, was dort gespeichert wurde. Steht dort „Alles in Ordnung.“, läuft die ganze Kette. Heißt es „Der Ping ist nicht angekommen.“, sind noch nicht alle Prozesse oben. Warte, bis sie im Dashboard laufen, und sende den Ping erneut. Nur die Migrationsschritte `planning-migrate` und `content-migrate` beenden sich nach der Migration, das ist gewollt.

Hängt der Start oder bricht er ab, hilft meist einer dieser Punkte:

- Docker Desktop läuft nicht. Dann hängen alle Services beim Warten auf die Datenbank.
- Der Start bricht mit „DNX could not restore or run aspire.cli@13.6.2“ ab. `dotnet run` lädt die Aspire-CLI per `dotnet dnx`, beim ersten Start auf einem Rechner und nach jedem Aspire-Update, und MSBuild wartet darauf nur 120 Sekunden. Hol den Download einmal ohne Zeitgrenze nach, mit der Version von `Aspire.AppHost.Sdk` aus `global.json` (Abschnitt `msbuild-sdks`, derzeit 13.6.2). Danach startet der AppHost normal.

  ```
  dotnet dnx --yes aspire.cli@13.6.2 -- --version
  ```

- Das Dashboard erreicht den Resource Service nicht. Dann stimmen die Dev-Zertifikate nicht. Führ `dotnet dev-certs https --clean` aus und danach `dotnet dev-certs https --trust`.
- Ein Build meldet gesperrte DLLs (MSB3027 oder MSB3021). Dann läuft noch ein AppHost. Beende ihn, bevor du baust.

## Doku

| Datei | Inhalt |
|---|---|
| [docs/spec/glossary.md](docs/spec/glossary.md) | Fachbegriffe mit genau einer Definition und ihrem Namen im Code |
| [docs/spec/non-goals.md](docs/spec/non-goals.md) | Was je Kernpunkt bewusst nicht gebaut wird |
| [docs/spec/object-model.md](docs/spec/object-model.md) | Kernobjekte und der Service, dem jedes gehört |
| [docs/spec/modes.md](docs/spec/modes.md) | Was in Übung, Einstufung und Prüfungsmodus gilt |
| [docs/spec/events.md](docs/spec/events.md) | Die Ereignisse zwischen den Services und der Vertrag des Lern-Nachweises |
| [docs/architecture.md](docs/architecture.md) | Services, Kommunikation, Aufbau eines Service |
| [docs/service-template.md](docs/service-template.md) | Einen neuen Service aus der Vorlage anlegen, Schritt für Schritt |
| [docs/service-internals.md](docs/service-internals.md) | Was ein Service aus der Vorlage mitbringt: Token, Datenbank, Nachrichten, Tests, Image |
| [docs/deploy.md](docs/deploy.md) | Ausrollen, Rollback, Migrationen, Speicher und Health im Deploy |
| [docs/secrets.md](docs/secrets.md) | Umgang mit Secrets und wie sie geprüft werden |
| [docs/branch-protection.md](docs/branch-protection.md) | Regeln für `main` und Pull Requests |
| [docs/licenses.md](docs/licenses.md) | Erlaubte Lizenzen, Liste der Drittkomponenten, Aufnahme neuer Komponenten |
| [docs/dependencies.md](docs/dependencies.md) | Update-Vorschläge, Schwachstellenprüfung und Umgang mit Funden |
| [docs/spec/quality.md](docs/spec/quality.md) | Qualitätsziele mit Zielwert und Messweise |
| [docs/spec/acceptance.md](docs/spec/acceptance.md) | Akzeptanzkriterien mit Testfall je Kernpunkt |
| [docs/decisions/outbox.md](docs/decisions/outbox.md) | Warum jeder Service eine eigene Outbox-Tabelle hat |
| [docs/decisions/broker.md](docs/decisions/broker.md) | Warum NATS JetStream die Integration Events verteilt |
| [docs/decisions/android-app.md](docs/decisions/android-app.md) | Die App mit der gemeinsamen UI auf einem Android-Gerät starten, Hindernisse und Grenzen |
