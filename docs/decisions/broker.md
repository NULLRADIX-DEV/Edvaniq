# Broker für Integration Events

Die Outbox eines Service hält seine Ereignisse fest, bis sie beim Broker sind ([outbox.md](outbox.md)). Der Broker verteilt sie an die Services, die darauf hören, auch an die, die gerade nicht laufen. Gesucht war ein Broker mit einer .NET-Bibliothek unter freier Lizenz, der auf einem kleinen Server neben MySQL und den Services Platz hat.

## Entscheidung

Edvaniq nutzt NATS mit JetStream und spricht ihn über `NATS.Net` an, ohne ein Messaging-Framework darüber. Server und Bibliothek stehen unter Apache-2.0. JetStream speichert jede Nachricht in einem Stream, bis der Consumer sie bestätigt, und stellt sie erneut zu, wenn die Bestätigung ausbleibt. Eine Nachricht mit einer `Nats-Msg-Id`, die der Stream schon kennt, verwirft er beim Senden. Als `Nats-Msg-Id` geht deshalb die `MessageId` aus der Outbox mit. Der Server ist ein einzelnes Go-Programm ohne weitere Laufzeit, und Aspire bringt mit `Aspire.Hosting.Nats` eine fertige Anbindung für den AppHost mit.

Ein Framework über dem Broker bringt vor allem Outbox, Inbox und eigene Konventionen fürs Routing mit. Die Outbox bauen wir aber selbst, weil die MySQL-Speicher von Rebus, Wolverine und Brighter auf `MySqlConnector` setzen und unsere Services auf `MySql.Data`. Übrig bliebe vom Framework nur Senden und Empfangen, und das deckt `NATS.Net` allein ab. MassTransit fällt weg, weil es ab Version 9 kommerziell ist, NServiceBus, weil seine Lizenz nicht zu den erlaubten gehört ([licenses.md](../licenses.md)).

RabbitMQ wäre lizenzrechtlich möglich gewesen: `RabbitMQ.Client` steht unter `Apache-2.0 OR MPL-2.0`, der Server unter MPL-2.0 als Server-Software. Er braucht aber die Erlang-Laufzeit und damit mehr Speicher auf dem Server, ohne dass wir etwas bekämen, was JetStream nicht kann. Kafka ist für einen einzelnen kleinen Server zu groß.

Jeder Service hat einen Stream, der nach ihm heißt, in Großbuchstaben (`PLANNING`). Seine Ereignisse liegen darunter als Subject `planning.<ereignis>`, mit dem Namen des Typs in Kleinbuchstaben. Den Stream legt der Absender beim Start an oder gleicht ihn an, es braucht ihn vorher nicht zu geben. Jeder Handler eines Services hat einen eigenen dauerhaften Consumer, `<service>-<handler>`, der Nachrichten einzeln bestätigt.

## Nachweis

Die Anbindung liegt in `BuildingBlocks.Messaging`, die Tests in `tests/BuildingBlocks/Edvaniq.BuildingBlocks.Messaging.IntegrationTests` laufen in der CI gegen NATS 2.15 und MySQL 9.7. In den `CrossServiceTests` laufen zwei Services in einem Prozess, jeder mit eigener Datenbank, und nur der Broker verbindet sie. Ein Ereignis kommt vom einen zum anderen, auch wenn der Empfänger beim Senden aus war und erst danach startet. Geht die Bestätigung nach dem Speichern verloren, stellt NATS das Ereignis noch einmal zu, und die Inbox verhindert eine zweite Wirkung. Dieselbe Strecke läuft auch mit dem Broker im Prozess (`InMemoryTransport`), den die Tests der Services statt NATS nutzen.

Die `InboxTests` zeigen, dass ein Handler und der Eintrag in `inbox_messages` im selben `SaveChanges` landen, dass eine Nachricht, die die Inbox kennt, nur bestätigt wird und dass zwei Handler desselben Ereignisses es je einmal behandeln. Die `NatsTransportTests` zeigen, dass der Stream seine Grenzen hat und dass eine `MessageId`, die der Stream schon kennt, beim Senden als gesendet zählt.

## Grenzen

JetStream erkennt doppelte Nachrichten nur innerhalb eines Zeitfensters, ohne eigene Einstellung zwei Minuten. Die Outbox kann eine Nachricht aber auch viel später noch einmal senden, wenn ihr Commit nach dem Senden scheitert. Jeder Consumer braucht deshalb seine Inbox, das Zeitfenster spart nur Arbeit. Verwirft JetStream eine Nachricht als doppelt, meldet er beim Senden `Duplicate`. Der Dispatcher wertet das als gesendet, denn die Nachricht liegt schon im Stream. `EnsureSuccess()` aus `NATS.Net` wirft in diesem Fall eine Exception.

Ein einzelner Consumer bekommt die Nachrichten eines Streams in der Reihenfolge, in der sie ankamen. Nach einer erneuten Zustellung oder mit mehreren Consumern gilt das nicht mehr, Consumer verlassen sich also wie bei der Outbox nicht auf die Reihenfolge. Eine Nachricht, die ein Handler nie verarbeiten kann, wird weiter zugestellt und im Log gemeldet, andere Nachrichten hält sie nicht auf.

`NATS.Net` arbeitet nur asynchron. Die Arbeit mit der Datenbank bleibt trotzdem synchron, weil `OpenAsync` von `MySql.Data` hängen bleiben kann.

Ein Stream hebt Nachrichten 7 Tage auf, höchstens 256 MB auf der Platte, danach fallen die ältesten weg. Beides lässt sich über `Messaging:StreamMaxAge` und `Messaging:StreamMaxBytes` ändern. Die Inbox behält ihre Einträge 14 Tage, länger als der Stream die Nachrichten. Die Adresse des Brokers kommt nur von außen, als `ConnectionStrings:nats`: Lokal setzt sie der AppHost. Auf dem Server läuft noch kein NATS, ein Service ohne Adresse sendet und empfängt nichts, und seine Ereignisse bleiben in der Outbox.
