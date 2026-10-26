namespace Edvaniq.ArchitectureTests;

// docs/spec/acceptance.md: every core point has two or three acceptance criteria with a test case.
public class AcceptanceTests
{
    [Fact]
    public void CorePoints_HaveCriteriaWithTestCase()
    {
        var corePoints = AcceptanceRule.CorePoints(File.ReadAllText(Path.Combine(Repository.Root, "docs", "spec", "non-goals.md")));
        var criteria = AcceptanceRule.Criteria(File.ReadAllText(Path.Combine(Repository.Root, "docs", "spec", "acceptance.md")));

        Rules.AssertNone(AcceptanceRule.Violations(corePoints, criteria), "Acceptance criteria", "docs/spec/acceptance.md");
        Assert.Contains(corePoints, point => point.Name == "MVP1 Konto und Anmeldung" && point.Milestones.SequenceEqual(["M2", "M5"]));
        Assert.InRange(criteria.Single(section => section.Name == "MVP1 Konto und Anmeldung").Rows.Count, 2, 3);
    }

    [Fact]
    public void CorePoints_TakeTheMilestonesOfTheScopeSentence()
    {
        var corePoints = AcceptanceRule.CorePoints("""
            ## Kernpunkte der MVP-Liste

            ### MVP1 Konto und Anmeldung

            Der MVP enthält Registrierung und Login (M2, M5).

            ### Stabilisierung

            Der MVP enthält einen Pilot (M6). Zuerst fallen die Statistik-Kacheln (MVP24).

            ## Nur im MVP-Plus

            ### MVP13 Adaptive Schwierigkeit

            Passt die Schwierigkeit an (M7).
            """);

        Assert.Equal(["MVP1 Konto und Anmeldung", "Stabilisierung"], corePoints.Select(point => point.Name));
        Assert.Equal(["M2", "M5"], corePoints[0].Milestones);
        Assert.Equal(["M6"], corePoints[1].Milestones);
    }

    [Fact]
    public void BrokenCorePoints_AreReported()
    {
        var corePoints = AcceptanceRule.CorePoints("""
            ### MVP1 Konto und Anmeldung

            Der MVP enthält Registrierung und Login (M2, M5).

            ### MVP1 Konto und Anmeldung

            Der MVP enthält Login (M2).

            ### Stabilisierung

            Der MVP enthält einen Pilot.
            """);

        var violations = AcceptanceRule.Violations(corePoints, []);

        Assert.Equal(
            [
                "MVP1 Konto und Anmeldung is 2 times in docs/spec/non-goals.md",
                "Stabilisierung names no milestone in docs/spec/non-goals.md",
                "MVP1 Konto und Anmeldung has no criteria",
                "MVP1 Konto und Anmeldung has no criteria",
                "Stabilisierung has no criteria",
            ],
            violations);
    }

    [Fact]
    public void BrokenSections_AreReported()
    {
        var criteria = AcceptanceRule.Criteria("""
            ## Kernpunkte der MVP-Liste

            ### MVP2 Lernziel anlegen

            | ID | Kriterium | Testfall | Messweise | Ab |
            | --- | :--- | --- | --- | --- |
            | A1 | Speichert alle Felder. | Gegeben ein Nutzer. Wenn er speichert, dann steht alles da. | Integrationstest | M2 |

            ### MVP4 Material hochladn

            | ID | Kriterium | Testfall | Messweise | Ab |
            |---|---|---|---|---|
            | A2 | Formate kommen an. | offen | Integrationstest | M2 |
            | A1 | Grenzen greifen. | Gegeben zehn Dokumente. Wenn ein elftes kommt, dann lehnt die App ab. | Integrationstest | später |

            ### MVP3 Art des Lernziels

            | ID | Kriterium | Testfall | Messweise | Ab |
            |---|---|---|---|---|
            | A3 | Themengerüst ohne Material. | Gegeben kein Material. Wenn der Nutzer es anfordert, dann kommt ein Vorschlag. | Integrationstest | M5 |
            | B4 | Kurz. |

            ### MVP2 Lernziel anlegen

            | ID | Kriterium | Testfall | Messweise | Ab |
            |---|---|---|---|---|
            | A5 | Prüft Eingaben. | Gegeben ein leerer Name. Wenn der Nutzer speichert, dann kommt ein Hinweis. | Komponententest | M2 |
            | A6 | Überspringt den Upload. | Gegeben der Assistent. Wenn der Nutzer überspringt, dann landet er in der Übersicht. | Komponententest | M3 |

            ## Ergänzungen

            | Keine | Tabelle eines Kernpunkts |
            """);
        (string, string[])[] corePoints =
        [
            ("MVP1 Konto und Anmeldung", ["M2", "M5"]),
            ("MVP2 Lernziel anlegen", ["M2"]),
            ("MVP3 Art des Lernziels", ["M2", "M5"]),
            ("MVP4 Material hochladen", ["M2", "M5"]),
            ("MVP6 Zusammenfassungen", ["M3"]),
        ];

        var violations = AcceptanceRule.Violations(corePoints, criteria);

        Assert.Equal(
            [
                "MVP1 Konto und Anmeldung has no criteria",
                "MVP4 Material hochladen has no criteria",
                "MVP6 Zusammenfassungen has no criteria",
                "MVP4 Material hochladn is no core point in docs/spec/non-goals.md",
                "MVP2 Lernziel anlegen has 2 sections",
                "MVP2 Lernziel anlegen needs two or three criteria, not 1",
                "A6 holds from M3, but MVP2 Lernziel anlegen is built in M2",
                "A2 has no test case in the form Gegeben, Wenn, Dann",
                "A1 holds from 'später', which is no milestone like M2",
                "'B4' is no ID like A1",
                "B4 has no test case in the form Gegeben, Wenn, Dann",
                "B4 holds from '', which is no milestone like M2",
                "A1 is used 2 times",
            ],
            violations);
    }
}