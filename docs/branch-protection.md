# Branch-Schutz

Was auf `main` liegt, kann jederzeit ausgerollt werden. Deshalb kommt dorthin nur Code, der geprüft ist und eine grüne CI hat. Zwei Rulesets sorgen dafür (Settings → Rules → Rulesets):

| Ruleset | Regeln | Ausnahme |
|---|---|---|
| `main-protect` | Änderungen nur per Pull Request. Die Pflicht-Checks „Build & test backend“, „Secret scan“ und „Container images“ müssen grün sein. Der Branch muss vor dem Merge auf dem Stand von `main` sein. Kein Force-Push, Löschen gesperrt. | keine, auch nicht für Admins |
| `main-review` | 1 Approval. Neue Commits machen alte Approvals ungültig. | Repo-Admins dürfen beim Merge auf das Review verzichten, solange es nur einen Entwickler gibt |

## Vom Branch nach main

1. Einen Branch von `main` anlegen und einen Pull Request gegen `main` öffnen.
2. Die CI baut, testet alle Testprojekte samt Architekturtests, sucht nach Secrets und baut die Container-Images. Hochgeladen werden die Images erst nach dem Merge.
3. Der andere Entwickler reviewt und approvt.
4. Mergen. Ist `main` inzwischen weiter, vorher „Update branch“ klicken, damit die CI den echten Stand nach dem Merge prüft.

Ein roter Check blockiert den Merge für alle, auch Admins können ihn nicht übergehen.

## Solange es nur einen Entwickler gibt

GitHub lässt niemanden den eigenen Pull Request approven. Solange nur ein Entwickler Mitglied der Org ist, hat `main-review` deshalb eine Ausnahme für Admins. Sobald der zweite Entwickler dazukommt, fällt sie weg: Im Ruleset `main-review` die Bypass list leeren.

## Pflicht-Checks pflegen

Die Architekturtests laufen im Job „Build & test backend“ mit, weil `dotnet test` die ganze Solution testet. Sie sind damit automatisch Pflicht. Soll ein neuer CI-Job Pflicht werden, trägst du seinen Namen in `main-protect` ein. Benennst du einen Pflicht-Job um, musst du auch das Ruleset anpassen, sonst wartet jeder Pull Request auf einen Check, der nie kommt.
