using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests;

/// <summary>
/// Builds the customer-facing document tests render against, and the JSON the API would have sent
/// for it.
/// </summary>
/// <remarks>
/// Hand-written, and the object and its JSON are built from the same values, so a test can assert
/// on a figure without restating it. Kept deliberately small: it is a fixture, not a second
/// implementation of the contract.
/// </remarks>
internal static class CustomerAngebotBuilder
{
    internal const string Number = "ANG-2026-00042";

    /// <summary>
    /// Two sections with three lines between them and two VAT rates — enough to exercise position
    /// numbering across sections, a specification present and absent, a non-integer quantity, and a
    /// multi-rate summary.
    /// </summary>
    internal static CustomerAngebot Typical(
        CustomerAngebotDecision decision = CustomerAngebotDecision.Pending) =>
        new(
            Number,
            decision,
            NetTotal: 1_650.00m,
            VatBreakdown:
            [
                new CustomerVatLine(Rate: 16m, VatAmount: 64.00m),
                new CustomerVatLine(Rate: 19m, VatAmount: 237.50m),
            ],
            GrossTotal: 1_951.50m,
            Sections:
            [
                new CustomerSection(
                    "Abriss",
                    Subtotal: 1_250.00m,
                    Items:
                    [
                        new CustomerItem("Wände abbrechen", "Nichttragend, inkl. Entsorgung", 10m, "m2", 25.00m, 250.00m),
                        new CustomerItem("Schutt entsorgen", null, 2.5m, "pauschal", 400.00m, 1_000.00m),
                    ]),
                new CustomerSection(
                    "Baustelleneinrichtung",
                    Subtotal: 400.00m,
                    Items:
                    [
                        new CustomerItem("Gerüst stellen", null, 1m, "Stk", 400.00m, 400.00m),
                    ]),
            ]);

    /// <summary>The JSON the API returns for <see cref="Typical"/>, in the API's own casing.</summary>
    internal static string TypicalJson(string decision = "Pending") => $$"""
        {
          "angebotNumber": "{{Number}}",
          "decision": "{{decision}}",
          "decisionAt": null,
          "netTotal": 1650.00,
          "vatBreakdown": [
            { "rate": 16, "vatAmount": 64.00 },
            { "rate": 19, "vatAmount": 237.50 }
          ],
          "grossTotal": 1951.50,
          "sections": [
            {
              "title": "Abriss",
              "subtotal": 1250.00,
              "items": [
                {
                  "description": "Wände abbrechen",
                  "specification": "Nichttragend, inkl. Entsorgung",
                  "quantity": 10,
                  "unit": "m2",
                  "unitPrice": 25.00,
                  "lineTotal": 250.00
                },
                {
                  "description": "Schutt entsorgen",
                  "specification": null,
                  "quantity": 2.5,
                  "unit": "pauschal",
                  "unitPrice": 400.00,
                  "lineTotal": 1000.00
                }
              ]
            },
            {
              "title": "Baustelleneinrichtung",
              "subtotal": 400.00,
              "items": [
                {
                  "description": "Gerüst stellen",
                  "specification": null,
                  "quantity": 1,
                  "unit": "Stk",
                  "unitPrice": 400.00,
                  "lineTotal": 400.00
                }
              ]
            }
          ]
        }
        """;

    /// <summary>A document whose only section carries no lines — reachable, see the section tests.</summary>
    internal static CustomerAngebot WithEmptySection() =>
        new(
            Number,
            CustomerAngebotDecision.Pending,
            NetTotal: 250.00m,
            VatBreakdown: [new CustomerVatLine(19m, 47.50m)],
            GrossTotal: 297.50m,
            Sections:
            [
                new CustomerSection("Abriss", 250.00m,
                    [new CustomerItem("Wände abbrechen", null, 10m, "m2", 25.00m, 250.00m)]),
                new CustomerSection("Noch offen", 0m, []),
            ]);

    /// <summary>A document carrying whatever free text a test needs rendered.</summary>
    internal static CustomerAngebot WithItemText(string description, string? specification) =>
        new(
            Number,
            CustomerAngebotDecision.Pending,
            NetTotal: 250.00m,
            VatBreakdown: [new CustomerVatLine(19m, 47.50m)],
            GrossTotal: 297.50m,
            Sections:
            [
                new CustomerSection("Abriss", 250.00m,
                    [new CustomerItem(description, specification, 10m, "m2", 25.00m, 250.00m)]),
            ]);
}
