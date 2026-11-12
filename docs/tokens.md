# Token-Vertrag

Jeder Service erkennt den Aufrufer allein am Token. Damit Identity wachsen kann, ohne dass ein Service sich ändert, ist hier festgehalten, was ein Token von Identity enthält und wie man es bekommt. Woher ein Service die Schlüssel holt und was er prüft, steht in der [Architektur](architecture.md#tokens) und unter [Token-Prüfung](service-internals.md#token-prüfung).

**Kompatibel zu M2:** Der Vollausbau von Identity (#244) stellt Tokens genau dieser Form aus. Neue Claims dürfen hinzukommen, die hier genannten bleiben mit derselben Bedeutung. Kein Service darf sich auf einen Claim verlassen, der hier nicht steht.

## Inhalt

Ein Token ist ein JWT mit dem Typ `at+jwt` nach RFC 9068, signiert mit ES256. Den Schlüssel kennt nur Identity, den öffentlichen Teil findet ein Service über das Discovery-Dokument unter `/.well-known/openid-configuration`. Der Header nennt mit `kid` den Schlüssel, er bleibt über Neustarts und Deploys gleich.

| Claim | Inhalt |
|---|---|
| `iss` | Aussteller, auf dem Server die Adresse der App, lokal der Wert aus dem AppHost |
| `aud` | `edvaniq-api`, für alle Services gleich, beim Token eines internen Clients `edvaniq-internal` ([Interne Tokens](#interne-tokens)) |
| `sub` | beim Login die ID des Kontos, eine GUID wie `3f2b9c1e-8d4a-4e0f-9b6a-2c7d1e5f4a10`, beim Client-Token die Client-ID |
| `client_id` | der Client, der das Token geholt hat: `web` oder `content` |
| `jti` | eine eigene ID je Token |
| `iat`, `nbf`, `exp` | ausgestellt, gültig ab, gültig bis |

Name, Mail-Adresse oder andere Angaben zur Person stehen nie im Token. Ein Service, der den Nutzer braucht, nimmt `sub` als `OwnerId`. Die Konto-ID ändert sich nie, auch nicht, wenn ein Konto ein neues Passwort bekommt.

## Gültigkeit

Das Token eines Logins gilt eine Stunde, ein Client-Token fünf Minuten (`Tokens:UserLifetime` und `Tokens:Lifetime` in der `appsettings.json` von Identity). Einen Refresh-Token gibt es noch nicht. Läuft das Token eines Nutzers ab, meldet er sich neu an. Kommt später ein Refresh-Token dazu, ändert sich am Access-Token nichts.

Wird ein Konto gelöscht, gilt sein Token nicht bis zum Ablauf weiter. Jeder Service mit Nutzerdaten merkt sich den gelöschten Nutzer, sobald `UserDeleted` bei ihm ankommt ([Nutzer löschen](service-internals.md#nutzer-löschen)), und lehnt danach jede Anfrage mit einem Token dieses Kontos mit 401 ab, wie bei einem ungültigen Token. Das Gateway prüft das nicht selbst, es reicht die 401 des Service unverändert weiter, und jede Route für Nutzer endet bei einem solchen Service. Der Web-Host beendet die Anmeldung, sobald das Backend das Token des Nutzers mit 401 ablehnt, so wie bei einem Logout. Tokens anderer Konten bleiben gültig.

Zwischen dem Löschen und der Ablehnung liegt höchstens eine Minute, solange Broker und Services laufen. Identity legt `UserDeleted` im selben Speichern wie die Löschung in die Outbox, der Dispatcher sendet jede Sekunde, und der Service speichert den gelöschten Nutzer zusammen mit dem Entfernen seiner Daten. Das dauert Sekunden, die Minute lässt Platz für eine erneute Zustellung. Ist der Broker länger weg, endet das Token spätestens mit seinem Ablauf. Belegt ist das mit `TokenOfADeletedAccount_IsRejectedEverywhere_WithinAMinute` in `Edvaniq.EventFlowTests`. Kann ein Service seine Datenbank nicht fragen, antwortet er mit einem Fehler, nie mit 401.

## Token holen

Beide Wege laufen über `POST /connect/token` mit einem Formular (`application/x-www-form-urlencoded`) nach RFC 6749. Das Gateway leitet den Endpunkt als `POST /identity/connect/token` weiter, der Browser erreicht ihn nie. Jede Anfrage trägt `client_id` und `client_secret` des Clients.

Der Login eines Teamkontos nutzt den Password-Grant: `grant_type=password` mit `username` und `password`. Erlaubt ist er nur einem Client, der in der Konfiguration `AllowPassword` hat, heute nur dem Web-Host `web`. Der Web-Host fragt ihn für den Login in der Web-Shell an und behält das Token bei sich, der Browser bekommt es nie zu sehen ([Architektur](architecture.md#tokens)). Groß- und Kleinschreibung und Leerzeichen am Rand des Namens zählen nicht, „Ben“ und „ben“ sind dasselbe Konto. Ein Client-Token holt der Web-Host mit `grant_type=client_credentials`.

Eine erfolgreiche Antwort ist `200` mit `access_token`, `token_type` (`Bearer`) und `expires_in` in Sekunden. Keine Antwort darf in einen Cache, deshalb steht in jeder `Cache-Control: no-store`.

| Antwort | `error` | Bedeutung |
|---|---|---|
| 400 | `invalid_request` | kein Formular, oder Name bzw. Passwort fehlen |
| 400 | `unsupported_grant_type` | ein anderer Grant als die beiden oben |
| 400 | `invalid_client` | Client unbekannt oder Secret falsch |
| 400 | `unauthorized_client` | der Client darf keinen Login anfragen |
| 400 | `invalid_grant` | Name oder Passwort falsch. Ob es das Konto gibt, sagt die Antwort nicht. |
| 429 | `too_many_attempts` | zu viele Versuche für dieses Konto in kurzer Zeit, `Retry-After` nennt die Wartezeit in Sekunden |

## Interne Tokens

Fragt ein Service einen anderen ohne Nutzer, nimmt er das Token eines eigenen Clients. Heute ist das nur der Client `content`: Der Worker von Content fragt Knowledge, welche IDs die Themen einer Analyse bekommen haben ([Fragenpool](architecture.md#fragenpool)). Der Client darf nur Client Credentials, keinen Login. Sein Token hat die Audience `edvaniq-internal` statt `edvaniq-api`, deshalb lehnen alle Services und das Gateway es an jedem normalen Endpunkt mit 401 ab. Es gilt nie als Nutzer, auch nicht als einer ohne Daten.

Ein Endpunkt, der ein internes Token annehmen soll, sagt das mit `RequireInternalClient("<client>")` aus `BuildingBlocks.Web`. Er nimmt dann nur ein Token mit der Audience `edvaniq-internal`, bei dem `client_id` und `sub` der genannte Client sind. Das Token eines Nutzers oder eines anderen Clients bekommt dort 401 oder 403. Ein solcher Endpunkt hat keine Route im Gateway und im Web-Host. Was er herausgibt, legt er selbst fest, ohne Nutzer aus dem Token: Knowledge gibt nur die Themen eines Dokuments heraus, und ein Dokument gehört genau einem Nutzer.

## Passwörter

Identity speichert ein Passwort nur als Hash, PBKDF2 mit HMAC-SHA256 und eigenem Salt je Konto. Der Hash nennt Verfahren und Rundenzahl, so kann eine spätere Version beides erhöhen und alte Hashes trotzdem prüfen. Ein Passwort hat 12 bis 256 Zeichen. Weder Passwort noch Hash noch Token stehen je im Log, das prüfen die `LoggingTests` von Identity.

## Teamkonto anlegen

Bis es eine Registrierung gibt, legt der Betreiber Teamkonten mit einem Befehl der Api an, so wie er auch die Migrationen startet. Es wird nichts von Hand in die Datenbank geschrieben.

```bash
dotnet Edvaniq.Services.Identity.Api.dll account set <name> < passwort.txt
```

Das Passwort kommt nur über die Standardeingabe, nie als Argument, sonst stünde es in der Prozessliste und der Shell-History. Ein Zeilenumbruch am Ende gehört nicht dazu. Gibt es das Konto schon, bekommt es das neue Passwort und behält seine ID, der Befehl lässt sich also beliebig oft ausführen. Er schreibt eine Zeile wie `created ben 3f2b9c1e-8d4a-4e0f-9b6a-2c7d1e5f4a10` oder `updated ben …` und endet mit 0. Ist der Name oder das Passwort ungültig, endet er mit 2 und nennt die Regel, bei einem anderen Fehler mit 1. Der Name besteht aus Kleinbuchstaben, Ziffern, Punkt, Bindestrich und Unterstrich, höchstens 64 Zeichen.

## Teamkonto löschen

```bash
dotnet Edvaniq.Services.Identity.Api.dll account delete <name>
```

Der Befehl entfernt das Konto und schickt im selben Speichern `UserDeleted` mit der ID des Kontos, also dem `sub` seiner Tokens. Daraufhin entfernt jeder Service alle Daten des Nutzers ([Nutzer löschen](service-internals.md#nutzer-löschen)). Gesendet wird über die Outbox: Der Befehl selbst braucht keinen Broker, die laufende Identity-Api schickt das Ereignis. Er schreibt nur `deleted <name>` und endet mit 0. Gibt es den Namen nicht, schreibt er `no account <name>`, schickt nichts und endet ebenfalls mit 0, ein zweiter Lauf ist also harmlos. Bei einem anderen Fehler endet er mit 1.