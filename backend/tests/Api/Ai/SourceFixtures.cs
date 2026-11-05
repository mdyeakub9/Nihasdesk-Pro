using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using PdfPageSize = UglyToad.PdfPig.Content.PageSize;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>Real files made on the spot, for a made-up Italian shop: nothing binary in the repo.</summary>
internal static class SourceFixtures
{
    public const string ItalianPage = """
        <!doctype html>
        <html lang="it"><head><title>Spedizioni e resi | Bottega Eval</title><script>alert('x')</script></head>
        <body>
          <nav><a href="/">Home</a> <a href="/prodotti">Prodotti</a> <a href="/contatti">Contatti</a></nav>
          <div id="cookie-banner" class="cookie-consent">Questo sito usa i cookie. Accetta tutti i cookie.</div>
          <article>
            <h1>Spedizioni e resi</h1>
            <p>Spediamo in tutta Italia con corriere tracciato. La consegna richiede di solito da due a quattro giorni lavorativi, a seconda della zona in cui ti trovi e del periodo dell'anno.</p>
            <h2>Isole</h2>
            <p>Per Sardegna e Sicilia la consegna può richiedere un giorno in più rispetto al resto d'Italia. Ti avvisiamo per email quando il pacco parte dal nostro laboratorio.</p>
            <h2>Resi</h2>
            <p>Il reso è gratuito entro quattordici giorni dalla consegna, se il prodotto non è stato usato e ha ancora la sua etichetta originale.</p>
            <table><tr><th>Zona</th><th>Giorni</th></tr><tr><td>Italia</td><td>2-4</td></tr><tr><td>Isole</td><td>3-5</td></tr></table>
          </article>
          <footer>Bottega Eval, tutti i diritti riservati. Privacy e termini.</footer>
        </body></html>
        """;

    /// <summary>An Italian PDF: a title, two headings, text on two pages, and optionally empty pages.</summary>
    public static byte[] Pdf(int emptyPages = 0)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var first = builder.AddPage(PdfPageSize.A4);
        first.AddText("Spedizioni", 22, new PdfPoint(50, 780), font);
        first.AddText("Spediamo in tutta Italia con corriere tracciato.", 11, new PdfPoint(50, 750), font);
        first.AddText("La consegna richiede da due a quattro giorni.", 11, new PdfPoint(50, 735), font);
        first.AddText("Resi", 22, new PdfPoint(50, 690), font);
        first.AddText("Il reso e gratuito entro quattordici giorni dalla consegna.", 11, new PdfPoint(50, 660), font);

        var second = builder.AddPage(PdfPageSize.A4);
        second.AddText("Il prodotto non deve essere stato usato e deve avere la sua etichetta.", 11, new PdfPoint(50, 780), font);

        for (var i = 0; i < emptyPages; i++)
        {
            builder.AddPage(PdfPageSize.A4);
        }

        return builder.Build();
    }

    /// <summary>
    /// The shape of a real shop's PDF: a header and footer on every page, wrapped lines at normal
    /// spacing, a sentence and a question with its answer cut by a page break.
    /// </summary>
    public static byte[] RealisticPdf()
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        PdfPageBuilder Page(int number)
        {
            var page = builder.AddPage(PdfPageSize.A4);
            page.AddText("NEGOZIO DEMO", 9, new PdfPoint(50, 810), font);
            page.AddText($"Documento interno v1.0 - pagina {number}", 9, new PdfPoint(50, 30), font);
            return page;
        }

        var first = Page(1);
        first.AddText("Domande frequenti", 20, new PdfPoint(50, 760), font);
        first.AddText("Q: Spedite fuori dall'Italia?", 11, new PdfPoint(50, 730), font);
        first.AddText("A: Non ancora. Spediamo solo agli indirizzi in Italia, secondo la copertura", 11, new PdfPoint(50, 716), font);
        first.AddText("del corriere e la disponibilita.", 11, new PdfPoint(50, 702), font);
        first.AddText("Q: Avete un negozio fisico?", 11, new PdfPoint(50, 90), font);

        var second = Page(2);
        second.AddText("A: No, solo online.", 11, new PdfPoint(50, 780), font);
        second.AddText("Il negozio lavora solo online e consegna con corriere tracciato in tutta", 11, new PdfPoint(50, 104), font);
        second.AddText("la penisola e nelle isole, con tempi che dipendono dalla zona e", 11, new PdfPoint(50, 90), font);

        var third = Page(3);
        third.AddText("dal periodo dell'anno.", 11, new PdfPoint(50, 780), font);
        third.AddText("Resi", 20, new PdfPoint(50, 740), font);
        third.AddText("Il reso e gratuito entro quattordici giorni.", 11, new PdfPoint(50, 710), font);
        third.AddText("Consegna", 11, new PdfPoint(50, 670), font);
        third.AddText("Costo", 11, new PdfPoint(300, 670), font);
        third.AddText("Standard", 11, new PdfPoint(50, 656), font);
        third.AddText("5,90 euro", 11, new PdfPoint(300, 656), font);
        third.AddText("Corriere rapido", 11, new PdfPoint(50, 642), font);
        third.AddText("9,90 euro", 11, new PdfPoint(300, 642), font);
        third.AddText("Le consegne partono dal lunedi al venerdi.", 11, new PdfPoint(50, 600), font);

        return builder.Build();
    }

    /// <summary>Pages with no text at all: what a scan looks like to a text reader.</summary>
    public static byte[] ScannedPdf()
    {
        using var builder = new PdfDocumentBuilder();
        builder.AddPage(PdfPageSize.A4);
        builder.AddPage(PdfPageSize.A4);
        return builder.Build();
    }

    public static byte[] LongPdf(int pages)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < pages; i++)
        {
            builder.AddPage(PdfPageSize.A4).AddText($"Pagina {i}", 11, new PdfPoint(50, 780), font);
        }

        return builder.Build();
    }

    /// <summary>A PDF whose trailer says it is encrypted, with keys that no empty password opens.</summary>
    public static byte[] EncryptedPdf()
    {
        var text = new StringBuilder();
        text.Append("%PDF-1.4\n");
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        text.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        text.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>\nendobj\n");
        text.Append("4 0 obj\n<< /Filter /Standard /V 1 /R 2 /O (");
        text.Append(new string('a', 32));
        text.Append(") /U (");
        text.Append(new string('b', 32));
        text.Append(") /P -44 >>\nendobj\n");
        var xref = text.Length;
        text.Append("trailer\n<< /Size 5 /Root 1 0 R /Encrypt 4 0 R /ID [<00> <00>] >>\nstartxref\n");
        text.Append(xref);
        text.Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    /// <summary>An Italian DOCX with heading styles, a list and a table.</summary>
    public static byte[] Docx()
    {
        using var buffer = new MemoryStream();
        using (var document = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(
                Styled("Title", "Guida del negozio"),
                Styled("Heading1", "Pagamenti"),
                new Paragraph(new Run(new Text("Si paga in contanti al corriere alla consegna."))),
                Styled("Heading2", "Metodi"),
                Item("Contrassegno"),
                Item("Bonifico per le aziende"),
                Styled("Heading1", "Zone di consegna"),
                new Table(
                    Row("Zona", "Costo"),
                    Row("Sardegna", "5 euro"),
                    Row("Italia", "7,90 euro"))));
            main.Document.Save();
        }

        return buffer.ToArray();
    }

    private static Paragraph Styled(string style, string text) =>
        new(new ParagraphProperties(new ParagraphStyleId { Val = style }), new Run(new Text(text)));

    private static Paragraph Item(string text) =>
        new(new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 })), new Run(new Text(text)));

    private static TableRow Row(params string[] cells) =>
        new(cells.Select(c => (OpenXmlElement)new TableCell(new Paragraph(new Run(new Text(c))))));
}
