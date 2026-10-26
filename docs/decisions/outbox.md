# Outbox für MySQL

Ein Service speichert eine Änderung und meldet sie anderen Services als Ereignis. Stirbt der Prozess zwischen Speichern und Senden, fehlt das Ereignis. Sendet er zuerst und das Speichern scheitert, kursiert ein Ereignis zu einer Änderung, die es nicht gibt. Beides verhindert eine Outbox.

## Entscheidung

Jeder Service hat in seiner eigenen Datenbank eine Tabelle `outbox_messages`. Ein Ereignis kommt dort im selben `SaveChanges` an wie die Änderung, zu der es gehört, also speichert MySQL beides oder keins von beidem. Ein Dispatcher im Prozess des Service holt die offenen Ereignisse in Stapeln ab, gibt sie an den Broker und markiert sie als gesendet. Er holt sie mit `SELECT … FOR UPDATE SKIP LOCKED`, damit ein zweiter Dispatcher nicht wartet, sondern die nächsten Zeilen nimmt.

Eine fertige Outbox aus einer Bibliothek passt nicht zu unserem Treiber. Die Services sprechen über Oracles `MySql.Data` mit MySQL, die MySQL-Speicher von Wolverine, CAP und Rebus bauen dagegen auf `MySqlConnector`. Über zwei Treiber hinweg gibt es keine gemeinsame Transaktion, und genau die braucht die Outbox. MassTransit fällt weg, weil es ab Version 9 kommerziell ist. Die eigene Tabelle hängt außerdem an keinem Broker: Der Dispatcher kennt nur eine Schnittstelle zum Senden, dahinter steckt NATS JetStream ([broker.md](broker.md)).

## Nachweis

Die Outbox liegt in `BuildingBlocks.Messaging`, und `ServiceDbContext` legt `outbox_messages` und die Inbox-Tabelle in jedem Service an. Die `OutboxTests` in `tests/BuildingBlocks/Edvaniq.BuildingBlocks.Messaging.IntegrationTests` laufen in der CI gegen eine echte MySQL 9.7, mit demselben Treiber und denselben Verbindungseinstellungen wie die Services. Sie zeigen, dass ein gescheitertes `SaveChanges` oder ein Rollback weder die Änderung noch das Ereignis hinterlässt und dass der Dispatcher Ereignisse einer offenen Transaktion nicht sieht. Ein Ereignis, das vor einem Absturz oder einem gescheiterten Senden gespeichert wurde, geht beim nächsten Lauf raus. Vier Dispatcher gleichzeitig senden 300 Ereignisse, jedes genau einmal. Ein weiterer Test deckt das Aufräumen alter Zeilen ab.

## Grenzen

Zugestellt wird mindestens einmal, nicht genau einmal. Scheitert der Commit nach dem Senden, geht der Stapel noch einmal raus. Jedes Ereignis trägt deshalb eine `MessageId`, an der ein Consumer erkennt, dass er es schon verarbeitet hat.

Ein Ereignis geht erst mit dem nächsten Lauf des Dispatchers raus, und solange er sendet, hält er die Zeilen seines Stapels gesperrt. Mit mehreren Dispatchern kann ein späteres Ereignis vor einem früheren ankommen, Consumer dürfen sich also nicht auf die Reihenfolge verlassen.

Der Dispatcher läuft jede Sekunde und sendet bis zu 50 Ereignisse je Runde. Nimmt der Broker ein Ereignis nicht an, endet die Runde dort: Er ist dann meist für alle weg, und die Reihenfolge bleibt erhalten. Die Zeile merkt sich Fehler und Versuche, und der Dispatcher probiert es mit wachsendem Abstand wieder. Ein Ereignis, das der Broker nie annimmt, hält deshalb die dahinter auf, bis jemand es behebt. Gesendete Zeilen löscht der Service nach 14 Tagen, ungesendete bleiben.
