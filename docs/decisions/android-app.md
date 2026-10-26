# Android-App mit gemeinsamer UI

Die Seiten von Edvaniq entstehen einmal in `Client.UI`. Im Browser hostet sie `Edvaniq.Web.Client`, auf dem Smartphone die App `Edvaniq.App` (MAUI Blazor Hybrid). Für Android war das bisher nur eine Annahme, denn die CI baut die App nicht, und auf einem echten Gerät lief sie noch nie. Ein Spike mit einer Zeitbox von einem Tag sollte zeigen, ob ein Debug-Build auf einem echten Gerät startet und die Shell zeigt. Login, Signierung und Verteilung gehörten nicht dazu.

## Ergebnis

Die App startet als Debug-Build auf einem OnePlus CPH2747 mit Android 16 (API 36) und zeigt die Shell aus `Client.UI` mit Kopfzeile, Menü und Fußzeile. Auch der Ping auf der Seite `/systemstatus` läuft aus der App bis in die Datenbank von Planning und zurück. Am Code war dafür nichts zu ändern. Weil Android SDK und JDK schon mit Visual Studio installiert waren, lief die App nach etwa zehn Minuten. Die gemeinsame UI trägt also auch auf Android, und eine spätere Test-APK baut auf demselben Projekt auf.

## Die App auf ein Gerät bringen

Du brauchst das .NET SDK aus `global.json` mit den Workloads für MAUI und Android, ein Android SDK mit der Plattform 36 und ein JDK 21. Am einfachsten kommt alles mit Visual Studio und der Workload „.NET Multi-Platform App UI“. Der Build findet SDK und JDK dann von selbst.

Am Gerät schaltest du in den Entwickleroptionen das USB-Debugging ein, verbindest es per USB und erlaubst dem Rechner den Zugriff. `adb devices` listet es dann als `device`. `adb` liegt im Android SDK unter `platform-tools` und steht nicht von selbst im `PATH`. Danach baut, installiert und startet ein Befehl die App:

```
dotnet build src/Clients/Edvaniq.App/Edvaniq.App.csproj -t:Run -f net10.0-android
```

Der Build signiert die APK mit dem Debug-Schlüssel des SDK, eine eigene Signierung braucht es dafür nicht. Der erste Lauf dauert etwa zwei Minuten.

## Hindernisse

Die Kopfzeile der Shell liegt unter der Statusleiste von Android, Benachrichtigungen und Akkustand stehen also über dem Logo und dem Menü. Seit Android 15 zeichnet eine App, die für API 35 oder höher gebaut ist, immer bis an den Bildschirmrand. Platz für die Statusleiste hält `wwwroot/app.css` der App aber nur auf iOS frei (`.status-bar-safe-area`). Bedienbar bleibt die Shell trotzdem.

Icon und Splash-Screen sind noch die lila Vorlage von MAUI.

## Grenzen

Die App spricht immer mit dem öffentlichen Web-Host `https://edvaniq.nullradix.de`, die Adresse steht fest in `MauiProgram.cs`. Die Seiten stammen aus dem lokalen Build, alle Daten aber vom Stand auf dem Server. Ein lokal gestartetes Backend erreicht die App nicht, denn `localhost` ist auf dem Gerät das Gerät selbst.

Die CI baut die App nicht, weil ihr Runner keine MAUI-Workload hat und sie das Projekt vor dem Build aus der Solution nimmt. Ob die App noch baut, zeigt also nur ein lokaler Build. Eine einstellbare Backend-Adresse, Login, eine signierte APK und ein Build in der CI kommen erst mit der Android-Test-APK nach dem MVP-Kern.
