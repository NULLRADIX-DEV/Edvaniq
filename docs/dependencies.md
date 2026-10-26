# Abhängigkeiten und Schwachstellen

Veraltete oder verwundbare Pakete und Images sollen auffallen, bevor sie auf dem Server laufen. Dafür sorgen Dependabot, der NuGet-Audit und Grype. Welche Komponenten überhaupt dazukommen dürfen, regelt `docs/licenses.md`.

## Update-Vorschläge

Dependabot öffnet jede Woche Pull Requests, eingestellt in `.github/dependabot.yml`:

- **NuGet** für Backend, Clients und Tests: Minor- und Patch-Updates gesammelt in einem Pull Request, jede neue Hauptversion in einem eigenen. `Microsoft.IdentityModel.JsonWebTokens` ist ausgenommen. Erfasst sind auch die SDKs unter `msbuild-sdks` in `global.json`, etwa `Aspire.AppHost.Sdk`. Ihre Version steht deshalb dort und nicht in der Projektdatei.
- **GitHub Actions**, gesammelt in einem Pull Request.
- **Images in `deploy/compose.yml`**, die nicht von uns stammen, etwa MySQL. Für MySQL schlägt Dependabot keine neue Hauptversion vor.

Die Datenbank bleibt auf der LTS-Linie von MySQL, derzeit 9.7. Das Tag `mysql:9.7` bekommt die Patches dieser Linie, sie kommen mit dem nächsten Deploy.

Jede neue Hauptversion stellt dagegen das Datenverzeichnis beim ersten Start um, und danach geht es nicht mehr zurück. Ein Rollback des Deploys startet die Datenbank dann nicht, es bleibt nur die Sicherung vor dem Deploy. Innovation-Releases wie 26.7 werden außerdem nur bis zum nächsten Release unterstützt. Den Wechsel auf das nächste LTS machst du deshalb von Hand in einem eigenen Pull Request, nachdem das Zurückspielen einer Sicherung geübt ist.

Für eine bekannte Lücke öffnet Dependabot außerdem sofort einen eigenen Pull Request (Security Updates) und zeigt sie unter Security → Dependabot.

Die **Basis-Images** der Prozesse wählt das SDK: `mcr.microsoft.com/dotnet/aspnet:10.0` für Web-Projekte, `mcr.microsoft.com/dotnet/runtime:10.0` für die Worker. Der Tag nennt keinen Patch-Stand, jeder Build holt also den aktuellen. Ein neues Basis-Image kommt mit jedem Merge auf `main` und dem nächsten Deploy, dafür braucht es keinen Vorschlag.

Nicht von Dependabot erfasst sind die Werkzeuge der CI, die als Variable in `.github/workflows/ci.yml` stehen: `GITLEAKS_VERSION` und `GRYPE_IMAGE`. Die hebst du von Hand an.

## Was jeder Pull Request prüft

Pull Requests von Dependabot laufen durch dieselbe CI mit denselben Pflicht-Checks wie alle anderen:

- Build und Tests, darunter die Architekturtests mit `LicenseTests` (Lizenz jeder Komponente) und `MySqlImageTests` (dasselbe MySQL-Image in AppHost, Tests und `deploy/compose.yml`).
- **NuGet-Audit:** Er prüft beim Restore jedes Paket, auch die indirekten. Eine Lücke der Stufe high oder critical macht den Restore zum Fehler (`NU1903`, `NU1904`), lokal und in der CI. So ist es am 14.10.2026 passiert, mit zwei Lücken in `Microsoft.AspNetCore.Components.WebAssembly.Server` 10.0.12. Moderate und niedrige Lücken bleiben Warnungen.
- **Grype** prüft die Pakete des Betriebssystems im Web-Image. Rot wird der Check bei einer Lücke der Stufe critical, für die es einen Fix gibt.
- Der Secret-Scan.

## Wöchentlicher Lauf

Montags um 05:00 UTC läuft die CI auf `main` auch ohne neuen Commit. Der NuGet-Audit prüft dann gegen die neuesten Meldungen, und Grype prüft ein frisch gebautes Basis-Image, hier schon ab der Stufe high. Images pusht dieser Lauf nicht. Ist er rot, gibt es eine neue Lücke, ohne dass sich am Code etwas geändert hat.

## Wenn etwas gefunden wird

1. **NuGet-Audit rot:** Hebe das Paket in `Directory.Packages.props` auf eine Version mit Fix an. Steckt die Lücke in einem indirekten Paket, trägst du dort eine `PackageVersion` mit der Version mit Fix ein. Sie gilt dann für alle Projekte, und weil sie damit ein direktes Paket ist, braucht sie eine Zeile in `docs/licenses.md`. Schreib in den Pull Request, ob Edvaniq betroffen war.
2. **Kein Fix verfügbar:** Ersetze das Paket, wenn die Lücke Edvaniq trifft. Trifft sie Edvaniq nachweislich nicht, darfst du sie mit `<NuGetAuditSuppress Include="<Advisory-URL>" />` in `Directory.Build.props` ausnehmen, mit Begründung im Kommentar und einem Issue, das die Ausnahme wieder entfernt.
3. **Grype rot:** Die Lücke steckt in einem Paket des Betriebssystems im Basis-Image. Oft hat Microsoft das Image schon neu gebaut, dann reicht es, den Lauf zu wiederholen. Sonst wartest du auf den Neubau. Ein Fund der Stufe high im wöchentlichen Lauf blockiert so lange keinen Merge.
4. **Pull Request von Dependabot:** Reviewe ihn wie jeden anderen. Bei einer neuen Hauptversion liest du die Änderungen und prüfst die Lizenz. Wird `LicenseTests` rot, hat sich die Lizenz geändert, dann gilt `docs/licenses.md`. Ändert sich das MySQL-Image in `deploy/compose.yml`, ziehst du im selben Pull Request `MySqlTestServer` nach und, falls Aspire noch das alte Image nimmt, den AppHost mit `WithImageTag`. Sonst bleibt `MySqlImageTests` rot.
