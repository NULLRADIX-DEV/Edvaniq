# Branch-Schutz

Was auf `main` liegt, kann jederzeit ausgerollt werden. Deshalb kommt dorthin nur Code, der geprüft ist und eine grüne CI hat. Zwei Rulesets sorgen dafür (Settings → Rules → Rulesets):

| Ruleset | Regeln |
|---|---|
| `main-protect` | Änderungen nur per Pull Request. Die Pflicht-Checks „Build & test backend“, „Secret scan“ und „Container images“ müssen grün sein. Der Branch muss vor dem Merge auf dem Stand von `main` sein. Kein Force-Push, Löschen gesperrt. |
| `main-review` | 1 Approval. Neue Commits machen alte Approvals ungültig. |

## Vom Branch nach main

1. Einen Branch von `main` anlegen und einen Pull Request gegen `main` öffnen.
2. Die CI baut, testet alle Testprojekte samt Architekturtests, sucht nach Secrets und baut die Container-Images. Die Tests laufen in mehreren Jobs parallel, der Job „Build & test backend“ fasst sie zusammen. Ändert der Pull Request keinen ausgelieferten Prozess (nichts unter `src/` oder `deploy/`, keine `Directory.*`, `global.json`, `nuget.config` und `ci.yml`), ist „Container images“ ohne Bau sofort grün. Auf `main` entstehen die Images immer, hochgeladen werden sie erst nach dem Merge.
3. Der andere Entwickler reviewt und approvt. Ändert der Pull Request, was ein Client zeigt, geht er dabei die Checkliste aus [docs/accessibility.md](accessibility.md) durch.
4. Mergen. Ist `main` inzwischen weiter, vorher „Update branch“ klicken, damit die CI den echten Stand nach dem Merge prüft.

Ein roter Check blockiert den Merge für alle, auch Admins können ihn nicht übergehen.

## Pflicht-Checks pflegen

Die Architekturtests haben im Job „Architecture & template“ einen eigenen Schritt „Architecture tests“. Der Pflicht-Job „Build & test backend“ ist nur grün, wenn dieser Job und alle Teile von „Tests“ grün sind, die Architekturtests sind damit automatisch Pflicht. Die Testjobs dulden Testprojekte ohne Tests (Exit-Code 8), dieser Schritt nicht: Er wird auch rot, wenn kein Architekturtest läuft oder das Projekt nicht gebaut ist. Dass ein Verstoß den Merge sperrt, zeigt [#801](https://github.com/NULLRADIX-DEV/Edvaniq/pull/801): Ein Verweis auf die Domain eines anderen Service machte den Schritt rot. Die Teile von „Tests“ findet die CI selbst: „services“ nimmt jedes Testprojekt aus `Edvaniq.slnx` außer den Browsertests und den Architekturtests, die Browsertests verteilt sie nach Testklassen auf drei Teile. Ein neues Testprojekt oder eine neue Testklasse läuft also ohne Änderung am Workflow mit. Soll ein neuer CI-Job Pflicht werden, nimmst du ihn in `needs` von „Build & test backend“ auf oder trägst seinen Namen in `main-protect` ein. Benennst du einen Pflicht-Job um, musst du auch das Ruleset anpassen, sonst wartet jeder Pull Request auf einen Check, der nie kommt.
