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
| `test_double` | nur beim Login eines Teamkontos für automatische Tests: `true`. Sonst fehlt der Claim ([Teamkonto für Tests](#teamkonto-für-tests)). |

Name, Mail-Adresse oder andere Angaben zur Person stehen nie im Token. Ein Service, der den Nutzer braucht, nimmt `sub` als `OwnerId`. Die Konto-ID ändert sich nie, auch nicht, wenn ein Konto ein neues Passwort bekommt.

## Gültigkeit

Das Token eines Logins gilt eine Stunde, ein Client-Token fünf Minuten (`Tokens:UserLifetime` und `Tokens:Lifetime` in der `appsettings.json` von Identity). Einen Refresh-Token gibt es noch nicht. Läuft das Token eines Nutzers ab, meldet er sich neu an. Kommt später ein Refresh-Token dazu, ändert sich am Access-Token nichts.

Wird ein Konto gelöscht, gilt sein Token nicht bis zum Ablauf weiter. Jeder Service mit Nutzerdaten merkt sich den gelöschten Nutzer, sobald `UserDeleted` bei ihm ankommt ([Nutzer löschen](service-internals.md#nutzer-löschen)), und lehnt danach jede Anfrage mit einem Token dieses Kontos mit 401 ab, wie bei einem ungültigen Token. Das Gateway prüft das nicht selbst, es reicht die 401 des Service unverändert weiter, und jede Route für Nutzer endet bei einem solchen Service. Der Web-Host beendet die Anmeldung, sobald das Backend das Token des Nutzers mit 401 ablehnt, so wie bei einem Logout. Tokens anderer Konten bleiben gültig.

Zwischen dem Löschen und der Ablehnung liegt höchstens eine Minute, solange Broker und Services laufen. Identity legt `UserDeleted` im selben Speichern wie die Löschung in die Outbox, der Dispatcher sendet jede Sekunde, und der Service speichert den gelöschten Nutzer zusammen mit dem Entfernen seiner Daten. Das dauert Sekunden, die Minute lässt Platz für eine erneute Zustellung. Ist der Broker länger weg, endet das Token spätestens mit seinem Ablauf. Belegt ist das mit `TokenOfADeletedAccount_IsRejectedEverywhere_WithinAMinute` in `Edvaniq.EventFlowTests`. Kann ein Service seine Datenbank nicht fragen, antwortet er mit einem Fehler, nie mit 401.

## Token holen

Beide Wege laufen über `POST /connect/token` mit einem Formular (`application/x-www-form-urlencoded`) nach RFC 6749. Das Gateway leitet den Endpunkt als `POST /identity/connect/token` weiter, der Browser erreicht ihn nie. Jede Anfrage trägt `client_id` und `client_secret` des Clients.

Der Login eines Kontos nutzt den Password-Grant: `grant_type=password` mit `username` und `password`. Erlaubt ist er nur einem Client, der in der Konfiguration `AllowPassword` hat, heute nur dem Web-Host `web`. Der Web-Host fragt ihn für den Login in der Web-Shell an und behält das Token bei sich, der Browser bekommt es nie zu sehen ([Architektur](architecture.md#tokens)). Ein Teamkonto meldet sich mit seinem Namen an, ein Nutzerkonto mit seiner E-Mail-Adresse. Enthält `username` ein `@`, gilt er als Adresse, ein Name hat nie eins (siehe [Kontoarten](#kontoarten)). Groß- und Kleinschreibung und Leerzeichen am Rand zählen bei beiden nicht, „Ben“ und „ben“ sind dasselbe Konto. Ein Client-Token holt der Web-Host mit `grant_type=client_credentials`.

Eine erfolgreiche Antwort ist `200` mit `access_token`, `token_type` (`Bearer`) und `expires_in` in Sekunden. Keine Antwort darf in einen Cache, deshalb steht in jeder `Cache-Control: no-store`.

| Antwort | `error` | Bedeutung |
|---|---|---|
| 400 | `invalid_request` | kein Formular, oder Name bzw. Passwort fehlen |
| 400 | `unsupported_grant_type` | ein anderer Grant als die beiden oben |
| 400 | `invalid_client` | Client unbekannt oder Secret falsch |
| 400 | `unauthorized_client` | der Client darf keinen Login anfragen |
| 400 | `invalid_grant` | Name, Adresse oder Passwort falsch. Ob es das Konto gibt, sagt die Antwort nicht. |
| 400 | `invalid_grant` mit `reason` | das Passwort stimmt, aber das Konto darf sich noch nicht anmelden: `unconfirmed` (Adresse nicht bestätigt) oder `awaiting_approval` (wartet auf die Freigabe). Nur mit dem richtigen Passwort kommt `reason` mit, sonst wüsste jeder, dass es die Adresse gibt. |
| 429 | `too_many_attempts` | zu viele Versuche für dieses Konto in kurzer Zeit, `Retry-After` nennt die Wartezeit in Sekunden |

## Kontoarten

Identity kennt zwei Arten von Konten, für die Token ist das gleich: Beide bekommen dasselbe Token mit ihrer Konto-ID als `sub`, mit denselben Claims, und kein Service merkt einen Unterschied.

- **Nutzerkonto:** meldet sich mit seiner E-Mail-Adresse an. Dazu kommen Pseudonym und Geburtsjahr. Adresse, Pseudonym und Geburtsjahr bleiben in Identity, sie stehen nie im Token und nie im Log, auch nicht in der Sperre für zu viele Versuche: Die zählt eine Adresse nur über ihren SHA-256-Hash. Ein neues Konto ist erst nutzbar, wenn die Adresse bestätigt und bei Minderjährigen die Freigabe erteilt ist. Registrierung, Bestätigung und Freigabe kommen mit eigenen Issues, bis dahin gibt es keine Nutzerkonten auf dem Server.
- **Teamkonto:** meldet sich mit einem Namen an, wird nur per Befehl angelegt und gelöscht (siehe unten) und ist immer aktiv. Es dient dem Team, dem Smoke-Test nach jedem Deploy und den Konten für automatische Tests.

Teamkonten bleiben neben den Nutzerkonten bestehen und behalten ihre ID, damit gehen auch ihre Daten in den Services nicht verloren. Das ist vorläufig, die Bestätigung durch den Betreiber steht noch aus (#244).

## Interne Tokens

Fragt ein Service einen anderen ohne Nutzer, nimmt er das Token eines eigenen Clients. Heute ist das nur der Client `content`: Der Worker von Content fragt Knowledge, welche IDs die Themen einer Analyse bekommen haben ([Fragenpool](architecture.md#fragenpool)). Der Client darf nur Client Credentials, keinen Login. Sein Token hat die Audience `edvaniq-internal` statt `edvaniq-api`, deshalb lehnen alle Services und das Gateway es an jedem normalen Endpunkt mit 401 ab. Es gilt nie als Nutzer, auch nicht als einer ohne Daten.

Ein Endpunkt, der ein internes Token annehmen soll, sagt das mit `RequireInternalClient("<client>")` aus `BuildingBlocks.Web`. Er nimmt dann nur ein Token mit der Audience `edvaniq-internal`, bei dem `client_id` und `sub` der genannte Client sind. Das Token eines Nutzers oder eines anderen Clients bekommt dort 401 oder 403. Ein solcher Endpunkt hat keine Route im Gateway und im Web-Host. Was er herausgibt, legt er selbst fest, ohne Nutzer aus dem Token: Knowledge gibt nur die Themen eines Dokuments heraus, und ein Dokument gehört genau einem Nutzer.

## Passwörter

Identity speichert ein Passwort nur als Hash, PBKDF2 mit HMAC-SHA256 und eigenem Salt je Konto. Der Hash nennt Verfahren und Rundenzahl, so kann eine spätere Version beides erhöhen und alte Hashes trotzdem prüfen. Ein Passwort hat 12 bis 256 Zeichen. Weder Passwort noch Hash noch Token stehen je im Log, das prüfen die `LoggingTests` von Identity.

## Teamkonto anlegen

Der Betreiber legt Teamkonten mit einem Befehl der Api an, so wie er auch die Migrationen startet. Es wird nichts von Hand in die Datenbank geschrieben.

```bash
dotnet Edvaniq.Services.Identity.Api.dll account set <name> < passwort.txt
```

Das Passwort kommt nur über die Standardeingabe, nie als Argument, sonst stünde es in der Prozessliste und der Shell-History. Ein Zeilenumbruch am Ende gehört nicht dazu. Gibt es das Konto schon, bekommt es das neue Passwort und behält seine ID, der Befehl lässt sich also beliebig oft ausführen. Er schreibt eine Zeile wie `created ben 3f2b9c1e-8d4a-4e0f-9b6a-2c7d1e5f4a10` oder `updated ben …` und endet mit 0. Ist der Name oder das Passwort ungültig, endet er mit 2 und nennt die Regel, bei einem anderen Fehler mit 1. Der Name besteht aus Kleinbuchstaben, Ziffern, Punkt, Bindestrich und Unterstrich, höchstens 64 Zeichen.

### Teamkonto für Tests

```bash
dotnet Edvaniq.Services.Identity.Api.dll account set <name> --test-double < passwort.txt
```

Mit `--test-double` ist das Konto für automatische Tests markiert, die Ausgabe endet dann mit `--test-double`. Seine Tokens tragen `test_double: true`. Content merkt sich das an jedem Dokument, das mit so einem Token eingefügt oder hochgeladen wird, und der Worker analysiert es mit der simulierten KI statt mit dem eingestellten Anbieter. So läuft ein Test gegen das ausgelieferte System ohne Kosten und mit festen Antworten. Setzen kann die Markierung nur dieser Befehl, keine Anfrage und kein Feld im Body. Ein `account set` ohne die Option nimmt sie wieder weg, schon eingefügte Dokumente behalten ihre.

## Teamkonto löschen

```bash
dotnet Edvaniq.Services.Identity.Api.dll account delete <name>
```

Der Befehl entfernt das Konto und schickt im selben Speichern `UserDeleted` mit der ID des Kontos, also dem `sub` seiner Tokens. Daraufhin entfernt jeder Service alle Daten des Nutzers ([Nutzer löschen](service-internals.md#nutzer-löschen)). Gesendet wird über die Outbox: Der Befehl selbst braucht keinen Broker, die laufende Identity-Api schickt das Ereignis. Er schreibt nur `deleted <name>` und endet mit 0. Gibt es den Namen nicht, schreibt er `no account <name>`, schickt nichts und endet ebenfalls mit 0, ein zweiter Lauf ist also harmlos. Bei einem anderen Fehler endet er mit 1.