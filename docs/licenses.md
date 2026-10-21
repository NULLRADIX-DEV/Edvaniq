# Lizenzen

Edvaniq nutzt nur Drittkomponenten mit geprüfter freier Lizenz. Drittkomponente ist alles, was nicht in diesem Repo entsteht: NuGet-Pakete, .NET-Tools, Container-Images, GitHub Actions und Programme in der CI, später auch Schriften, Icons, KI-Modelle und Demo-Material. Hier stehen die Prüfregel, die Liste aller Komponenten und der Weg, auf dem eine neue dazukommt.

Die Web-Shell zeigt die Liste unter `/open-source` mit Komponente und Lizenz, ohne Login. Diese Datei ist dafür in `Edvaniq.Client.UI` eingebettet, die Seite zeigt also immer den Stand des Commits, aus dem gebaut wurde. Wer Überschriften oder Spalten der Liste umbenennt, ändert damit auch die Seite.

## Prüfregel

Lizenzen werden mit ihrer [SPDX-Kennung](https://spdx.org/licenses/) angegeben.

**Erlaubt** sind `MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `ISC`, `Zlib` und `0BSD`, für Schriften zusätzlich `OFL-1.1`.

Ein Ausdruck mit `AND` ist erlaubt, wenn jeder Teil erlaubt ist, einer mit `OR`, wenn mindestens ein Teil erlaubt ist.

**Ausgeschlossen** sind:

- `GPL`, `AGPL` und `SSPL` für Code, der in einem Edvaniq-Prozess läuft oder in einem Edvaniq-Image steckt.
- Lizenzen, die Geld kosten oder die Nutzung begrenzen: kommerzielle Lizenzen, Grenzen bei Umsatz oder Nutzerzahl und „source available“ wie `BUSL-1.1`, `Elastic-2.0` oder Commons Clause. Beispiele sind MassTransit ab v9 und iText.
- Komponenten ohne erkennbare Lizenz.

**Nur mit Einzelfreigabe** kommt eine Komponente dazu, deren Lizenz weder erlaubt noch ausgeschlossen ist, etwa `LGPL`, `MPL-2.0` oder die Lizenzbedingungen des Herstellers einer Zielplattform. Sie steht dann mit Begründung in der Liste, ihr Einsatz beginnt mit „Einzelfreigabe:“.

**Server-Software**, die als eigener Prozess unverändert läuft und nicht in ein Edvaniq-Image gebaut wird, darf unter `GPL` stehen, etwa MySQL. Sie steht trotzdem in der Liste, ihr Einsatz beginnt mit „Server-Software:“.

Indirekte NuGet-Pakete, also Abhängigkeiten der gelisteten Pakete, stehen nicht einzeln in der Liste, solange ihre Lizenz erlaubt ist und als SPDX-Kennung im Paket steht. Alle anderen stehen mit Namen da.

## Liste

### NuGet-Pakete

| Komponente | Lizenz | Einsatz |
|---|---|---|
| `Aspire.AppHost.Sdk` | MIT | SDK des AppHost |
| `Aspire.Hosting.MySql` | MIT | MySQL und phpMyAdmin im AppHost |
| `Aspire.Hosting.Testing` | MIT | Ende-zu-Ende- und Architekturtests |
| `bunit` | MIT | Komponententests der Clients |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | MIT | Token-Prüfung der Services |
| `Microsoft.AspNetCore.Components.Web` | MIT | UI-Bibliothek |
| `Microsoft.AspNetCore.Components.WebAssembly` | MIT | Web-Client |
| `Microsoft.AspNetCore.Components.WebAssembly.Server` | MIT | Web-Host |
| `Microsoft.AspNetCore.Components.WebView.Maui` | MIT | MAUI-App |
| `Microsoft.AspNetCore.Mvc.Testing` | MIT | Integrationstests |
| `Microsoft.EntityFrameworkCore` | MIT | EF Core, Version zentral festgelegt |
| `Microsoft.EntityFrameworkCore.Design` | MIT | Migrationen |
| `Microsoft.EntityFrameworkCore.Relational` | MIT | EF Core, Version zentral festgelegt |
| `Microsoft.Extensions.Diagnostics.HealthChecks` | MIT | DB-Prüfung für Health |
| `Microsoft.Extensions.Hosting` | MIT | Worker |
| `Microsoft.Extensions.Http.Resilience` | MIT | ServiceDefaults |
| `Microsoft.Extensions.Logging.Debug` | MIT | MAUI-App |
| `Microsoft.Extensions.ServiceDiscovery` | MIT | ServiceDefaults |
| `Microsoft.Extensions.ServiceDiscovery.Yarp` | MIT | Gateway, Web-Host |
| `Microsoft.IdentityModel.JsonWebTokens` | MIT | Tokens in Tests |
| `Microsoft.Maui.Controls` | MIT | MAUI-App |
| `MySql.Data` | GPL-2.0-only WITH Universal-FOSS-exception-1.0 | Vorläufige Ausnahme: DB-Verbindung der Services, siehe [Vorläufige Ausnahmen](#vorläufige-ausnahmen) |
| `MySql.EntityFrameworkCore` | GPL-2.0-only WITH Universal-FOSS-exception-1.0 | Vorläufige Ausnahme: EF-Provider der Services, siehe [Vorläufige Ausnahmen](#vorläufige-ausnahmen) |
| `NATS.Net` | Apache-2.0 | Broker-Anbindung im Broker-Spike |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | Apache-2.0 | ServiceDefaults |
| `OpenTelemetry.Extensions.Hosting` | Apache-2.0 | ServiceDefaults |
| `OpenTelemetry.Instrumentation.AspNetCore` | Apache-2.0 | ServiceDefaults |
| `OpenTelemetry.Instrumentation.Http` | Apache-2.0 | ServiceDefaults |
| `OpenTelemetry.Instrumentation.Runtime` | Apache-2.0 | ServiceDefaults |
| `Testcontainers.MySql` | MIT | Integrationstests |
| `Testcontainers.Nats` | MIT | NATS in den Tests des Broker-Spikes |
| `xunit.v3.extensibility.core` | Apache-2.0 | Tests |
| `xunit.v3.mtp-v2` | Apache-2.0 | Tests |
| `Yarp.ReverseProxy` | MIT | Gateway, Web-Host |

### Indirekte NuGet-Pakete

Diese Pakete kommen über ein gelistetes Paket dazu und haben keine erlaubte SPDX-Kennung im Paket. Ihre Lizenz ist von Hand geprüft.

| Komponente | Lizenz | Einsatz |
|---|---|---|
| `Fractions` | BSD-2-Clause | über Aspire, nur AppHost und Tests. Die Lizenz liegt nur als Datei im Paket. |
| `K4os.Compression.LZ4` | MIT | über `MySql.Data`. Die Lizenz steht nur als Link im Paket. |
| `K4os.Compression.LZ4.Streams` | MIT | über `MySql.Data`. Die Lizenz steht nur als Link im Paket. |
| `K4os.Hash.xxHash` | MIT | über `MySql.Data`. Die Lizenz steht nur als Link im Paket. |
| `Microsoft.Graphics.Win2D` | Lizenzbedingungen von Microsoft | Einzelfreigabe: über MAUI, nur die App unter Windows. Kostenlos, Bestandteil der Zielplattform. |
| `Microsoft.Web.WebView2` | Lizenzbedingungen von Microsoft | Einzelfreigabe: über MAUI, nur die App unter Windows. Kostenlos, Bestandteil der Zielplattform. |
| `Microsoft.Windows.SDK.BuildTools` | Lizenzbedingungen von Microsoft | Einzelfreigabe: über MAUI, nur der Build der App unter Windows. Kostenlos, Bestandteil der Zielplattform. |
| `Microsoft.WindowsAppSDK` | Lizenzbedingungen von Microsoft | Einzelfreigabe: über MAUI, nur die App unter Windows. Kostenlos, Bestandteil der Zielplattform. |

### Tools und Programme

| Komponente | Lizenz | Einsatz |
|---|---|---|
| .NET SDK und Runtime | MIT | Build und Laufzeit aller Prozesse |
| `aspire.cli` | MIT | Start des AppHost mit `dotnet run` |
| `dotnet-ef` | MIT | Migrationen anlegen (`dotnet-tools.json`) |
| gitleaks | MIT | Secret-Scan in der CI |

### GitHub Actions

| Komponente | Lizenz | Einsatz |
|---|---|---|
| `actions/cache` | MIT | CI |
| `actions/checkout` | MIT | CI und Deploy |
| `actions/setup-dotnet` | MIT | CI |

### Container-Images

| Komponente | Lizenz | Einsatz |
|---|---|---|
| Grype (`anchore/grype`) | Apache-2.0 | Schwachstellen-Scan der Images in der CI |
| `mcr.microsoft.com/dotnet/aspnet` | MIT | Basis der Edvaniq-Images mit ASP.NET Core. Die Pakete des Betriebssystems darin stehen unter ihren eigenen Lizenzen. |
| `mcr.microsoft.com/dotnet/runtime` | MIT | Basis der Images der Worker. Die Pakete des Betriebssystems darin stehen unter ihren eigenen Lizenzen. |
| `mysql` | GPL-2.0-only | Server-Software: Datenbank auf dem Server, lokal und in Integrationstests, läuft unverändert als eigener Prozess. |
| `nats` | Apache-2.0 | Broker in den Tests des Broker-Spikes |
| `phpmyadmin` | GPL-2.0-only | Server-Software: nur lokal über Aspire, läuft unverändert als eigener Prozess. |

## Vorläufige Ausnahmen

`MySql.Data` und `MySql.EntityFrameworkCore` stehen unter `GPL-2.0-only WITH Universal-FOSS-exception-1.0` und stecken in jedem Service mit Datenbank, also auch in dessen Image. Nach der Prüfregel wäre das ausgeschlossen. Sie sind trotzdem vorläufig freigegeben, weil `MySql.EntityFrameworkCore` der festgelegte EF-Provider ist: Die freie Alternative Pomelo unterstützt nur EF Core 9. Ob die Ausnahme der Lizenz für Edvaniq reicht, ist noch nicht geklärt. Bis dahin kommt keine weitere Komponente unter `GPL` dazu.

## Neue Komponente aufnehmen

1. Vor dem ersten Einsatz die Lizenz feststellen. Bei NuGet steht sie als SPDX-Ausdruck in der nuspec, sonst in der LICENSE-Datei des Projekts. Steht dort nur ein Link oder eine Datei, liest du den Text und bestimmst die passende SPDX-Kennung.
2. Mit der Prüfregel vergleichen. Ist die Lizenz ausgeschlossen, nimm eine Alternative. Ist sie unklar, kommt die Komponente nicht ins Repo, bis ein Issue mit Label `legal` sie entschieden hat.
3. Im selben Pull Request eine Zeile in die passende Tabelle eintragen, bei einer Einzelfreigabe mit Begründung. Ein indirektes Paket kommt nur dazu, wenn seine Lizenz nicht erlaubt ist oder nicht als SPDX-Kennung im Paket steht.
4. Die CI prüft die Liste (siehe unten), der Reviewer die Begründung und alles, was die CI nicht sieht.
5. Bei einem Update auf eine neue Hauptversion nachsehen, ob sich die Lizenz geändert hat. MassTransit zum Beispiel ist mit v9 kommerziell geworden.
6. Fällt eine Komponente weg, kommt ihre Zeile mit raus.

## Wie geprüft wird

`LicenseTests` in `Edvaniq.ArchitectureTests` gleicht diese Datei bei jedem Pull Request im Pflicht-Schritt „Architecture tests“ mit dem Repo ab. Jede Abweichung steht als eigene Zeile in der Meldung, und schon eine macht die CI rot:

- ein direktes Paket aus `Directory.Packages.props`, ein SDK unter `msbuild-sdks` in `global.json` oder ein SDK mit Version in einer Projektdatei ohne Zeile unter „NuGet-Pakete“, oder eine Zeile dort ohne solches Paket
- ein Paket aus `obj/project.assets.json` eines Projekts in `Edvaniq.slnx`, dessen nuspec eine andere Lizenz nennt als die Liste
- ein indirektes Paket ohne Zeile, dessen Lizenz nicht erlaubt ist oder nur als Datei oder Link im Paket steht
- eine Zeile mit nicht erlaubter Lizenz, deren Einsatz nicht mit „Einzelfreigabe:“, „Vorläufige Ausnahme:“ oder „Server-Software:“ beginnt
- ein Tool aus `dotnet-tools.json`, eine Action aus `.github/workflows` oder ein Image aus `deploy`, den Workflows oder `tests` ohne Zeile

Nicht sehen kann der Test die Basis-Images des SDK, die Images, die Aspire selbst holt, und Programme, die ein Workflow herunterlädt, wie gitleaks. Die prüft der Reviewer. Der Test liest die Überschriften, Spalten und den Absatz „Erlaubt“ dieser Datei. Benenne sie nicht um, ohne den Test anzupassen.

Selbst nachsehen kannst du nach einem Restore so:

```
dotnet test --project tests/Edvaniq.ArchitectureTests --filter-class "*LicenseTests"
```
