using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentFailureCode : Migration
    {
        // The texts as they were stored up to this migration, frozen here so later wording changes don't move old rows.
        private static readonly (string Code, string[] Texts)[] Known =
        [
            ("NoText",
            [
                "Der Text enthält nichts, woraus sich lernen lässt. Füge einen anderen Text ein.",
                "Die Datei enthält keinen lesbaren Text, vermutlich ist sie ein Scan oder Foto. Lade eine Textversion hoch, etwa das Original als PDF mit Text oder als Word-Datei, oder füge den Text ein.",
                "Der Text enthält zu wenig lesbaren Inhalt, etwa weil er aus einem Scan stammt. Füge einen Text mit ganzen Sätzen ein, zum Beispiel aus einer Textversion des Materials.",
            ]),
            ("Unreadable",
            [
                "Die Datei ließ sich nicht lesen. Lade sie noch einmal hoch, zum Beispiel neu gespeichert.",
                "Die Datei ließ sich nicht verarbeiten. Speichere sie neu oder teile sie in kleinere Dateien auf und lade sie noch einmal hoch.",
                "Die Datei ist mit einem Passwort geschützt. Entferne den Schutz und lade sie noch einmal hoch.",
                "Die Datei lässt sich nicht lesen, weil sie ungewöhnlich stark gepackt ist. Speichere sie neu, zum Beispiel als PDF, und lade sie noch einmal hoch.",
                "Die Datei braucht zu lange zum Lesen. Teile sie in kleinere Dateien auf und lade sie einzeln hoch.",
            ]),
            ("TooLarge",
            [
                "Die Datei enthält zu viel Text. Erlaubt sind höchstens 200.000 Zeichen, teile sie auf.",
                "Die Datei hat mehr als 100 Seiten. Teile sie in kleinere Dateien auf, etwa nach Kapiteln, und lade sie einzeln hoch.",
            ]),
            ("LimitReached",
            [
                "Das KI-Kontingent für diesen Monat ist aufgebraucht. Versuch es im nächsten Monat noch einmal.",
            ]),
            ("AiUnavailable",
            [
                "KI derzeit nicht verfügbar. Versuch es später noch einmal.",
                "KI derzeit pausiert. Versuch es später noch einmal.",
            ]),
        ];

        // The daily quotas fill numbers into their text, only the start is fixed.
        private static readonly string[] LimitStarts =
        [
            "Dein Tageskontingent reicht für dieses Dokument nicht: ",
            "Dein Tageskontingent an Generierungen reicht für dieses Dokument nicht: ",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "Documents",
                type: "varchar(24)",
                maxLength: 24,
                nullable: true);

            foreach (var (code, texts) in Known)
            {
                migrationBuilder.Sql(
                    $"UPDATE `Documents` SET `FailureCode` = '{code}' WHERE `Status` = 'Failed' AND `FailureReason` IN ({string.Join(", ", texts.Select(Quoted))});");
            }

            foreach (var start in LimitStarts)
            {
                migrationBuilder.Sql(
                    $"UPDATE `Documents` SET `FailureCode` = 'LimitReached' WHERE `Status` = 'Failed' AND `FailureCode` IS NULL AND `FailureReason` LIKE {Quoted(start + "%")};");
            }

            migrationBuilder.Sql("UPDATE `Documents` SET `FailureCode` = 'Unknown' WHERE `Status` = 'Failed' AND `FailureCode` IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureCode",
                table: "Documents");
        }

        private static string Quoted(string text) => $"'{text.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}