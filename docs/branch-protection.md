# Edvaniq – Branch-Schutz

`main` ist die Deploy-Quelle. Dorthin kommt nur geprüfter Code mit grüner CI. Zwei Rulesets schützen den Branch (Settings → Rules → Rulesets):

| Ruleset | Regeln | Ausnahme |
|---|---|---|
| `main-protect` | Änderungen nur per Pull Request. Die Pflicht-Checks „Build & test backend“, „Secret scan“ und „Container images“ müssen grün sein. Der Branch muss vor dem Merge auf dem Stand von `main` sein. Kein Force-Push, Löschen gesperrt. | keine, auch nicht für Admins |
| `main-review` | 1 Approval. Neue Commits machen alte Approvals ungültig. | Repo-Admins dürfen beim Merge eines PR auf das Review verzichten (Übergang, siehe unten) |

## Ablauf

1. Branch von `main` anlegen, Pull Request gegen `main` öffnen.
2. Die CI baut, testet (alle Testprojekte, auch die Architekturtests), prüft auf Secrets und baut die Container-Images. Gepusht werden die Images erst nach dem Merge auf `main`.
3. Der andere Entwickler reviewt und approvt.
4. Mergen. Ist `main` inzwischen weiter, vorher „Update branch“, damit die CI den echten Merge-Stand prüft.

Ein roter Check blockiert den Merge für alle. Auch Admins können ihn nicht übergehen.

## Übergang: nur ein Entwickler in der Org

Solange nur ein Entwickler Mitglied der Org ist, gibt es in `main-review` die Admin-Ausnahme, denn GitHub lässt niemanden den eigenen PR approven. Sobald der zweite Entwickler Mitglied ist, wird die Ausnahme entfernt (Ruleset `main-review` → Bypass list leeren).

## Pflicht-Checks pflegen

- Die Architekturtests laufen im Job „Build & test backend“ mit (`dotnet test` über die ganze Solution) und sind damit automatisch Pflicht.
- Soll ein neuer CI-Job Pflicht sein, seinen Namen in `main-protect` eintragen.
- Wird ein Pflicht-Job umbenannt, muss das Ruleset angepasst werden. Sonst wartet jeder PR auf einen Check, der nie kommt.
