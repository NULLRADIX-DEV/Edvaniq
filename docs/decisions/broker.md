# Broker für Integration Events

Die Outbox eines Service hält seine Ereignisse fest, bis sie beim Broker sind ([outbox.md](outbox.md)). Der Broker verteilt sie an die Services, die darauf hören, auch an die, die gerade nicht laufen. Gesucht war ein Broker mit einer .NET-Bibliothek unter freier Lizenz, der auf einem kleinen Server neben MySQL und den Services Platz hat.

## Entscheidung

Edvaniq nutzt NATS mit JetStream und spricht ihn über `NATS.Net` an, ohne ein Messaging-Framework darüber. Server und Bibliothek stehen unter Apache-2.0. JetStream speichert jede Nachricht in einem Stream, bis der Consumer sie bestätigt, und stellt sie erneut zu, wenn die Bestätigung ausbleibt. Eine Nachricht mit einer `Nats-Msg-Id`, die der Stream schon kennt, verwirft er beim Senden. Als `Nats-Msg-Id` geht deshalb die `MessageId` aus der Outbox mit. Der Server ist ein einzelnes Go-Programm ohne weitere Laufzeit, und Aspire bringt mit `Aspire.Hosting.Nats` eine fertige Anbindung für den AppHost mit.

Ein Framework über dem Broker bringt vor allem Outbox, Inbox und eigene Konventionen fürs Routing mit. Die Outbox bauen wir aber selbst, weil die MySQL-Speicher von Rebus, Wolverine und Brighter auf `MySqlConnector` setzen und unsere Services auf `MySql.Data`. Übrig bliebe vom Framework nur Senden und Empfangen, und das deckt `NATS.Net` allein ab. MassTransit fällt weg, weil es ab Version 9 kommerziell ist, NServiceBus, weil seine Lizenz nicht zu den erlaubten gehört ([licenses.md](../licenses.md)).

RabbitMQ wäre lizenzrechtlich möglich gewesen: `RabbitMQ.Client` steht unter `Apache-2.0 OR MPL-2.0`, der Server unter MPL-2.0 als Server-Software. Er braucht aber die Erlang-Laufzeit und damit mehr Speicher auf dem Server, ohne dass wir etwas bekämen, was JetStream nicht kann. Kafka ist für einen einzelnen kleinen Server zu groß.

## Nachweis

Der Prototyp in `tests/Spikes/Edvaniq.Spikes.Broker` läuft in der CI gegen NATS 2.15 und MySQL 9.7. Der Test sendet wie der Dispatcher der Outbox, der Consumer `Edvaniq.Spikes.Broker.Consumer` läuft als eigener Prozess. Für jede Nachricht trägt er die `MessageId` in die Tabelle `inbox_messages` ein, im selben `SaveChanges` wie seine Wirkung, und bestätigt erst danach beim Broker. Kennt die Inbox die `MessageId` schon, bestätigt er nur.

Die Tests zeigen, dass eine Nachricht vom einen Prozess beim anderen ankommt und dass eine Nachricht, die gesendet wurde, während der Consumer aus war, nach seinem Start ankommt. Wird dieselbe `MessageId` zweimal gesendet, verwirft JetStream die zweite. Stirbt der Consumer nach dem Commit, aber vor der Bestätigung, stellt JetStream die Nachricht dem neu gestarteten Consumer noch einmal zu, und die Inbox verhindert eine zweite Wirkung.

## Grenzen

JetStream erkennt doppelte Nachrichten nur innerhalb eines Zeitfensters, ohne eigene Einstellung zwei Minuten. Die Outbox kann eine Nachricht aber auch viel später noch einmal senden, wenn ihr Commit nach dem Senden scheitert. Jeder Consumer braucht deshalb seine Inbox, das Zeitfenster spart nur Arbeit. Verwirft JetStream eine Nachricht als doppelt, meldet er beim Senden `Duplicate`. Der Dispatcher wertet das als gesendet, denn die Nachricht liegt schon im Stream. `EnsureSuccess()` aus `NATS.Net` wirft in diesem Fall eine Exception.

Ein einzelner Consumer bekommt die Nachrichten eines Streams in der Reihenfolge, in der sie ankamen. Nach einer erneuten Zustellung oder mit mehreren Consumern gilt das nicht mehr, Consumer verlassen sich also wie bei der Outbox nicht auf die Reihenfolge. Die Inbox wächst mit jeder Nachricht, bis jemand alte Einträge löscht.

`NATS.Net` arbeitet nur asynchron. Die Arbeit mit der Datenbank bleibt trotzdem synchron, weil `OpenAsync` von `MySql.Data` hängen bleiben kann.

Wie Streams und Subjects heißen, wer sie anlegt, wie lange sie Nachrichten aufheben und wie sich die Services am Broker anmelden, legt die echte Anbindung in der Service-Vorlage fest. Sie ersetzt auch den Prototyp (#151).
