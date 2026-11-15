# Testprotokoll: Upload bis 20 MB

Eine Datei bis 20 MB muss über die öffentliche Domain bis zu Content kommen, auch über eine langsame Mobilfunkverbindung. Eine größere muss mit der verständlichen Größenmeldung abgewiesen werden, ohne Fehlerseite. Die Tests im Repo prüfen jede Stelle einzeln ([Upload-Grenze](../architecture.md#upload-grenze)), dieses Protokoll prüft den ganzen Weg nach einem Deploy. Es gehört zu Issue #319.

## Vorbereitung

Du brauchst ein Testkonto auf der App und `curl` 7.76 oder neuer. Lade nur die Dateien hoch, die hier entstehen: Sie bestehen aus einem PDF-Kopf und Nullen, kommen durch den Upload und scheitern danach im Worker als nicht lesbar, ohne die KI zu erreichen. Jede zählt zu den 10 Dokumenten des Lernziels, lösch sie nach dem Test in der App.

Eine Datei von genau 20 MB legst du unter Windows in PowerShell so an:

```powershell
$f = [IO.File]::Create("upload-20mb.pdf"); $h = [Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n"); $f.Write($h, 0, $h.Length); $f.SetLength(20MB); $f.Close()
```

Unter Linux geht es mit `printf '%%PDF-1.4\n' > upload-20mb.pdf && truncate -s 20M upload-20mb.pdf`.

## Ablauf

1. **Skript ohne Drosselung.** Die Zugangsdaten kommen nur aus der Umgebung, nie als Argument:

   ```bash
   EDVANIQ_ACCOUNT=<konto> EDVANIQ_PASSWORD=<passwort> deploy/upload-check.sh https://<domain>
   ```

   Das Skript lädt drei Dateien hoch und gibt je Datei Status, Dauer und gesendete Bytes aus: genau 20 MB (erwartet 201), 20 MB plus ein Byte (erwartet 413 mit dem Titel „Die Datei ist zu groß. Erlaubt sind höchstens 20 MB.“) und 25 MB (erwartet 413). Es endet mit 0, wenn alle drei wie erwartet antworten.

2. **Skript mit langsamer Leitung.** Derselbe Aufruf mit `100K` als zweitem Argument begrenzt den Upload auf etwa 0,8 Mbit/s. Die 20 MB brauchen dann gut drei Minuten.

3. **Browser mit Drosselung.** In Chrome die Entwicklertools öffnen, im Tab „Network“ unter „Throttling“ ein eigenes Profil mit 1 Mbit/s Upload, 2 Mbit/s Download und 100 ms Latenz anlegen und auswählen. Dann auf der Seite „Themen“ `upload-20mb.pdf` hochladen. Die Seite zeigt „Wird hochgeladen …“, bis die Datei angekommen ist, danach erscheint das Dokument in der Liste.

4. **Browser über dem Limit.** Eine Datei mit 20 MB plus ein Byte auswählen. Die Seite lehnt sie ab, ohne etwas zu schicken, und zeigt die Größenmeldung. Im Tab „Network“ erscheint keine Anfrage an `documents/files`.

5. **Echtes Material.** Eine echte PDF mit Textebene knapp unter 20 MB hochladen und warten, bis sie „bereit“ ist oder mit einem fachlichen Grund scheitert (etwa zu viele Seiten). Ein Abbruch beim Hochladen zählt als Fehler.

## Ergebnis

Commit: `319ad55`
Datum: 16.11.2026
Geprüft von: Team, mit einem Testkonto
Leitung (Ort, Netz): Rechenzentrum über die öffentliche Domain, langsame Leitung per `curl --limit-rate`

| Schritt | Datei | Drosselung | Erwartet | Status | Dauer | Meldung | Ok |
|---|---|---|---|---|---|---|---|
| 1 | 20 MB | keine | 201 | 201 | 1,4 s | | ja |
| 1 | 20 MB + 1 Byte | keine | 413 mit Größenmeldung | 413 | 0,8 s | „Die Datei ist zu groß. Erlaubt sind höchstens 20 MB.“ | ja |
| 1 | 25 MB | keine | 413 | 413 | 0,1 s | 0 Bytes gesendet | ja |
| 2 | 20 MB | 100K | 201 | 201 | 207 s | | ja |
| 2 | 20 MB + 1 Byte | 100K | 413 mit Größenmeldung | 413 | 206 s | „Die Datei ist zu groß. Erlaubt sind höchstens 20 MB.“ | ja |
| 3 | 20 MB | Browser, 1 Mbit/s | Dokument in der Liste | | | | offen |
| 4 | 20 MB + 1 Byte | Browser | Größenmeldung, keine Anfrage | | | | offen |
| 5 | echte PDF | keine | bereit oder fachlicher Grund | | | | offen |

Bemerkungen: Die Testdateien kamen an und scheiterten danach im Worker wie erwartet als nicht lesbar. Bei 25 MB lehnte der Weg die Datei ab, bevor ein Byte gesendet war. Schritte 3 und 4 decken im Repo `HttpLearningMaterialTests.Upload_GoesThroughTheClientForFiles` (Upload ohne die 20-s-Grenze der übrigen Aufrufe) und `UploadTests.FileOver20MB_IsRefusedInTheBrowser_WithoutUpload` ab, die Prüfung am Gerät steht noch aus, ebenso Schritt 5.
