# Outbox für MySQL

Ein Service speichert eine Änderung und meldet sie anderen Services als Ereignis. Stirbt der Prozess zwischen Speichern und Senden, fehlt das Ereignis. Sendet er zuerst und das Speichern scheitert, kursiert ein Ereignis zu einer Änderung, die es nicht gibt. Beides verhindert eine Outbox.

## Entscheidung

Jeder Service hat in seiner eigenen Datenbank eine Tabelle `outbox_messages`. Ein Ereignis kommt dort im selben `SaveChanges` an wie die Änderung, zu der es gehört, also speichert MySQL beides oder keins von beidem. Ein Dispatcher im Prozess des Service holt die offenen Ereignisse in Stapeln ab, gibt sie an den Broker und markiert sie als gesendet. Er holt sie mit `SELECT … FOR UPDATE SKIP LOCKED`, damit ein zweiter Dispatcher nicht wartet, sondern die nächsten Zeilen nimmt.

Eine fertige Outbox aus einer Bibliothek passt nicht zu unserem Treiber. Die Services sprechen über Oracles `MySql.Data` mit MySQL, die MySQL-Speicher von Wolverine, CAP und Rebus bauen dagegen auf `MySqlConnector`. Über zwei Treiber hinweg gibt es keine gemeinsame Transaktion, und genau die braucht die Outbox. MassTransit fällt weg, weil es ab Version 9 kommerziell ist. Die eigene Tabelle hängt außerdem an keinem Broker: Der Dispatcher kennt nur eine Schnittstelle zum Senden, welche Bibliothek dahinter steckt, entscheidet sich getrennt (#150).

## Nachweis

Der Prototyp in `tests/Spikes/Edvaniq.Spikes.Outbox` läuft in der CI gegen eine echte MySQL 9.7, mit demselben Treiber und denselben Verbindungseinstellungen wie die Services. Seine Tests zeigen, dass ein gescheitertes `SaveChanges` oder ein Rollback weder die Änderung noch das Ereignis hinterlässt und dass der Dispatcher Ereignisse einer offenen Transaktion nicht sieht. Ein Ereignis, das vor einem Absturz oder einem gescheiterten Senden gespeichert wurde, geht beim nächsten Lauf raus. Vier Dispatcher gleichzeitig senden 300 Ereignisse, jedes genau einmal.

## Grenzen

Zugestellt wird mindestens einmal, nicht genau einmal. Scheitert der Commit nach dem Senden, geht der Stapel noch einmal raus. Jedes Ereignis trägt deshalb eine `MessageId`, an der ein Consumer erkennt, dass er es schon verarbeitet hat.

Ein Ereignis geht erst mit dem nächsten Lauf des Dispatchers raus, und solange er sendet, hält er die Zeilen seines Stapels gesperrt. Mit mehreren Dispatchern oder nach einem Fehlschlag kann ein späteres Ereignis vor einem früheren ankommen, Consumer dürfen sich also nicht auf die Reihenfolge verlassen. Gesendete Zeilen bleiben stehen, bis jemand sie löscht.

Wie oft der Dispatcher läuft, wie groß ein Stapel ist und wann alte Zeilen verschwinden, legt die echte Outbox in der Service-Vorlage fest. Sie ersetzt dann auch den Prototyp (#151).
