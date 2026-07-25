using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Draw;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;
using Vigma.TimbradoGateway.ViewModels.Timbrados;

namespace Vigma.TimbradoGateway.Services;

/// <summary>
/// Genera representación impresa (PDF) de un CFDI 4.0 desde xmlTimbrado + Adicionales JSON.
/// Soporta facturas regulares (I/E/T/N) y Complemento de Pago (P).
/// </summary>
public class FacturaPdfService
{
    private readonly IWebHostEnvironment _env;

    // ── Paleta Vigma ───────────────────────────────────────────
    private static readonly DeviceRgb CVigma = new(91, 91, 214);
    private static readonly DeviceRgb CDark = new(25, 25, 55);
    private static readonly DeviceRgb CGray = new(110, 110, 140);
    private static readonly DeviceRgb CLightBg = new(245, 245, 252);
    private static readonly DeviceRgb CBorder = new(210, 210, 230);
    private static readonly DeviceRgb CDanger = new(192, 50, 70);
    private static readonly DeviceRgb CSuccess = new(17, 153, 142);
    private static readonly DeviceRgb CRowAlt = new(248, 248, 255);
    private static readonly DeviceRgb CWhite = new(255, 255, 255);
    private static readonly DeviceRgb CTableHdr = new(55, 55, 120);
    private static readonly DeviceRgb CPagoHdr = new(17, 120, 110);

    public FacturaPdfService(IWebHostEnvironment env) => _env = env;

    // ─────────────────────────────────────────────────────────────
    //  ENTRADA PÚBLICA
    // ─────────────────────────────────────────────────────────────
    public byte[] GenerarPdf(TimbradoDetalleVM vm, string? tenantLogoRelPath = null)
    {
        var cfdi = ParsearCfdi(vm.XmlTimbrado ?? "");
        var adicionales = ParsearAdicionales(vm.Adicionales);
        var logoPath = ResolverLogo(tenantLogoRelPath);

        var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        var pdf = new PdfDocument(writer);
        var doc = new Document(pdf, PageSize.LETTER);
        doc.SetMargins(36f, 42f, 36f, 42f);

        var fR = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        var fB = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

        // Encabezado compartido
        AddHeader(doc, cfdi, vm, logoPath, fR, fB);
        AddEmisorReceptor(doc, cfdi, fR, fB);

        if (cfdi.TipoDeComprobante == "P")
        {
            // Layout especial para Complemento de Pago
            AddComplementoPago(doc, cfdi, fR, fB);
        }
        else
        {
            // Layout estándar
            AddConceptos(doc, cfdi, fR, fB);
            AddTotales(doc, cfdi, fR, fB);
        }

        AddSep(doc);
        AddTimbre(doc, cfdi, fR, fB);

        if (adicionales.Count > 0)
            AddAdicionales(doc, adicionales, fR, fB);

        AddFooter(doc, fR);

        doc.Close();
        return ms.ToArray();
    }

    // ─────────────────────────────────────────────────────────────
    //  ENCABEZADO
    // ─────────────────────────────────────────────────────────────
    private static void AddHeader(Document doc, CfdiData cfdi, TimbradoDetalleVM vm,
        string? logoPath, PdfFont fR, PdfFont fB)
    {
        var table = new Table(UnitValue.CreatePercentArray(new float[] { 54f, 46f }))
            .UseAllAvailableWidth()
            .SetBorder(Border.NO_BORDER)
            .SetMarginBottom(6f);

        // ── Izquierda: logo + emisor ───────────────────────────
        var left = new Cell().SetBorder(Border.NO_BORDER)
            .SetPaddingRight(12f)
            .SetVerticalAlignment(VerticalAlignment.MIDDLE);

        if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
        {
            try
            {
                left.Add(new Image(ImageDataFactory.Create(logoPath))
                    .SetMaxHeight(60f).SetMaxWidth(185f).SetMarginBottom(6f));
            }
            catch { /* omitir si falla */ }
        }

        left.Add(new Paragraph(cfdi.EmisorNombre)
            .SetFont(fB).SetFontSize(10.5f).SetFontColor(CDark).SetMarginBottom(2f));
        left.Add(new Paragraph($"RFC: {cfdi.EmisorRfc}")
            .SetFont(fR).SetFontSize(8f).SetFontColor(CGray).SetMarginBottom(1f));
        left.Add(new Paragraph($"Régimen: {cfdi.EmisorRegimenFiscal}")
            .SetFont(fR).SetFontSize(8f).SetFontColor(CGray).SetMarginBottom(1f));
        if (!string.IsNullOrWhiteSpace(cfdi.LugarExpedicion))
            left.Add(new Paragraph($"C.P. Expedición: {cfdi.LugarExpedicion}")
                .SetFont(fR).SetFontSize(8f).SetFontColor(CGray));

        // ── Derecha: tipo de comprobante + datos del CFDI ──────
        var right = new Cell().SetBorder(Border.NO_BORDER)
            .SetTextAlignment(TextAlignment.RIGHT)
            .SetVerticalAlignment(VerticalAlignment.MIDDLE);

        // Chip tipo de comprobante (usa background en el Paragraph, más compatible)
        var tipoLabel = TipoLabel(cfdi.TipoDeComprobante);
        var tipoBg = cfdi.TipoDeComprobante == "E" ? CDanger
                      : cfdi.TipoDeComprobante == "P" ? CPagoHdr
                      : CVigma;

        right.Add(new Paragraph(tipoLabel)
            .SetFont(fB).SetFontSize(12f).SetFontColor(CWhite)
            .SetBackgroundColor(tipoBg)
            .SetTextAlignment(TextAlignment.CENTER)
            .SetPaddingTop(5f).SetPaddingBottom(5f)
            .SetMarginBottom(8f));

        // Serie / Folio
        if (!string.IsNullOrWhiteSpace(cfdi.Serie) || !string.IsNullOrWhiteSpace(cfdi.Folio))
            right.Add(new Paragraph($"Serie: {cfdi.Serie}  |  Folio: {cfdi.Folio}")
                .SetFont(fR).SetFontSize(9f).SetFontColor(CDark)
                .SetTextAlignment(TextAlignment.RIGHT));

        // Fecha de emisión del comprobante
        if (!string.IsNullOrWhiteSpace(cfdi.Fecha))
            right.Add(new Paragraph($"Fecha: {cfdi.Fecha}")
                .SetFont(fR).SetFontSize(9f).SetFontColor(CDark)
                .SetTextAlignment(TextAlignment.RIGHT));

        // Moneda
        var moneda = cfdi.Moneda;
        if (!string.IsNullOrWhiteSpace(cfdi.TipoCambio)
            && cfdi.TipoCambio != "1" && cfdi.TipoCambio != "1.00")
            moneda += $"  T.C.: {cfdi.TipoCambio}";
        right.Add(new Paragraph(moneda)
            .SetFont(fR).SetFontSize(8f).SetFontColor(CGray)
            .SetTextAlignment(TextAlignment.RIGHT));

        // Chip cancelada
        if (vm.Cancelada)
            right.Add(new Paragraph("  CANCELADA  ")
                .SetFont(fB).SetFontSize(9f).SetFontColor(CWhite)
                .SetBackgroundColor(CDanger)
                .SetTextAlignment(TextAlignment.CENTER)
                .SetMarginTop(5f));

        table.AddCell(left);
        table.AddCell(right);
        doc.Add(table);
        doc.Add(new LineSeparator(new SolidLine(1.5f)).SetMarginBottom(10f));
    }

    // ─────────────────────────────────────────────────────────────
    //  EMISOR / RECEPTOR
    // ─────────────────────────────────────────────────────────────
    private static void AddEmisorReceptor(Document doc, CfdiData cfdi, PdfFont fR, PdfFont fB)
    {
        var table = new Table(UnitValue.CreatePercentArray(new float[] { 50f, 50f }))
            .UseAllAvailableWidth()
            .SetBorder(Border.NO_BORDER)
            .SetMarginBottom(12f);

        // Tarjeta Emisor
        var em = new Cell()
            .SetBorder(new SolidBorder(CBorder, 0.5f))
            .SetPadding(10f).SetPaddingRight(14f)
            .SetBackgroundColor(CLightBg);

        em.Add(SLabel("DATOS DEL EMISOR", fB));
        em.Add(DR("RFC:", cfdi.EmisorRfc, fR, fB));
        em.Add(DR("Nombre:", cfdi.EmisorNombre, fR, fB));
        em.Add(DR("Régimen fiscal:", cfdi.EmisorRegimenFiscal, fR, fB));
        em.Add(DR("C.P. expedición:", cfdi.LugarExpedicion, fR, fB));
        if (!string.IsNullOrWhiteSpace(cfdi.Exportacion))
            em.Add(DR("Exportación:", cfdi.Exportacion, fR, fB));

        // Tarjeta Receptor
        // Forma/Método de pago NO van aquí (ya aparecen en Totales)
        var re = new Cell()
            .SetBorder(new SolidBorder(CBorder, 0.5f))
            .SetPadding(10f).SetPaddingLeft(14f)
            .SetBackgroundColor(CLightBg);

        re.Add(SLabel("DATOS DEL RECEPTOR", fB));
        re.Add(DR("RFC:", cfdi.ReceptorRfc, fR, fB));
        re.Add(DR("Nombre:", cfdi.ReceptorNombre, fR, fB));
        re.Add(DR("Régimen fiscal:", cfdi.ReceptorRegimenFiscal, fR, fB));
        re.Add(DR("Dom. fiscal:", cfdi.ReceptorDomicilioFiscal, fR, fB));
        re.Add(DR("Uso CFDI:", cfdi.ReceptorUsoCfdi, fR, fB));

        table.AddCell(em);
        table.AddCell(re);
        doc.Add(table);
    }

    // ─────────────────────────────────────────────────────────────
    //  CONCEPTOS (facturas I/E/T/N)
    // ─────────────────────────────────────────────────────────────
    private static void AddConceptos(Document doc, CfdiData cfdi, PdfFont fR, PdfFont fB)
    {
        doc.Add(STitle("CONCEPTOS", fB));

        // Descuento: mostrar columna si el comprobante tiene Descuento o algún concepto tiene descuento
        bool hasDesc =
            (!string.IsNullOrWhiteSpace(cfdi.Descuento) && cfdi.Descuento != "0.00" && cfdi.Descuento != "0")
            || cfdi.Conceptos.Any(c =>
                !string.IsNullOrWhiteSpace(c.Descuento) && c.Descuento != "0.00" && c.Descuento != "0");

        string[] hdrs = hasDesc
            ? new[] { "#", "Descripción", "Clave Prod.", "Unidad", "Cantidad", "Val. Unitario", "Importe", "Descuento" }
            : new[] { "#", "Descripción", "Clave Prod.", "Unidad", "Cantidad", "Val. Unitario", "Importe" };
        float[] cols = hasDesc
            ? new float[] { 3.5f, 24f, 10f, 8f, 8f, 12f, 12f, 10f }
            : new float[] { 3.5f, 29f, 11f, 9f, 9f, 14f, 14f };

        var t = new Table(UnitValue.CreatePercentArray(cols))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(8f);

        foreach (var h in hdrs)
        {
            TextAlignment a = h is "Val. Unitario" or "Importe" or "Descuento"
                ? TextAlignment.RIGHT
                : (h is "#" or "Cantidad" ? TextAlignment.CENTER : TextAlignment.LEFT);
            t.AddHeaderCell(HdrCell(h, a, fB));
        }

        bool alt = false;
        int n = 1;
        foreach (var c in cfdi.Conceptos)
        {
            var bg = alt ? CRowAlt : CWhite; alt = !alt;

            t.AddCell(DCell(n++.ToString(), bg, fR, TextAlignment.CENTER));
            t.AddCell(DCell(c.Descripcion, bg, fR));
            t.AddCell(DCell(c.ClaveProdServ, bg, fR));
            t.AddCell(DCell(string.IsNullOrWhiteSpace(c.Unidad) ? c.ClaveUnidad : c.Unidad, bg, fR));
            t.AddCell(DCell(FmtDec(c.Cantidad), bg, fR, TextAlignment.RIGHT));
            t.AddCell(DCell(FmtMon(c.ValorUnitario), bg, fR, TextAlignment.RIGHT));
            t.AddCell(DCell(FmtMon(c.Importe), bg, fR, TextAlignment.RIGHT));
            if (hasDesc)
                t.AddCell(DCell(
                    string.IsNullOrWhiteSpace(c.Descuento) ? "—" : FmtMon(c.Descuento),
                    bg, fR, TextAlignment.RIGHT));
        }
        doc.Add(t);
    }

    // ─────────────────────────────────────────────────────────────
    //  TOTALES (facturas I/E/T/N)
    // ─────────────────────────────────────────────────────────────
    private static void AddTotales(Document doc, CfdiData cfdi, PdfFont fR, PdfFont fB)
    {
        var outer = new Table(UnitValue.CreatePercentArray(new float[] { 55f, 45f }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(14f);

        // Izquierda: datos del pago + certificado (una sola vez)
        var left = new Cell().SetBorder(Border.NO_BORDER).SetPaddingRight(10f);
        if (!string.IsNullOrWhiteSpace(cfdi.FormaPago))
            left.Add(DR("Forma de pago:", cfdi.FormaPago, fR, fB));
        if (!string.IsNullOrWhiteSpace(cfdi.MetodoPago))
            left.Add(DR("Método de pago:", cfdi.MetodoPago, fR, fB));
        if (!string.IsNullOrWhiteSpace(cfdi.Condiciones))
            left.Add(DR("Condiciones:", cfdi.Condiciones, fR, fB));
        if (!string.IsNullOrWhiteSpace(cfdi.NoCertificado))
            left.Add(DR("No. certificado:", Trunc(cfdi.NoCertificado, 30), fR, fB));

        // Derecha: tabla de importes
        var totals = new Table(UnitValue.CreatePercentArray(new float[] { 58f, 42f }))
            .UseAllAvailableWidth()
            .SetBorder(new SolidBorder(CBorder, 0.5f));

        void TRow(string lbl, string val, bool isTotal = false)
        {
            var bg = isTotal ? CVigma : CWhite;
            var fc = isTotal ? CWhite : CDark;
            var fn = isTotal ? fB : fR;
            float fs = isTotal ? 10.5f : 8.5f;
            float pv = isTotal ? 7f : 4f;

            totals.AddCell(new Cell()
                .SetBackgroundColor(bg).SetBorder(Border.NO_BORDER)
                .SetPaddingTop(pv).SetPaddingBottom(pv).SetPaddingLeft(10f)
                .Add(new Paragraph(lbl).SetFont(fn).SetFontSize(fs).SetFontColor(fc)));
            totals.AddCell(new Cell()
                .SetBackgroundColor(bg).SetBorder(Border.NO_BORDER)
                .SetPaddingTop(pv).SetPaddingBottom(pv).SetPaddingRight(10f)
                .Add(new Paragraph(val).SetFont(fn).SetFontSize(fs).SetFontColor(fc)
                    .SetTextAlignment(TextAlignment.RIGHT)));
        }

        TRow("Subtotal:", FmtMon(cfdi.SubTotal));

        // Descuento global del comprobante
        if (!string.IsNullOrWhiteSpace(cfdi.Descuento)
            && cfdi.Descuento != "0.00" && cfdi.Descuento != "0")
            TRow("Descuento:", $"- {FmtMon(cfdi.Descuento)}");

        foreach (var tr in cfdi.Traslados)
        {
            var lbl = ImpLabel(tr.Impuesto);
            if (decimal.TryParse(tr.TasaOCuota,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var tasa))
                lbl += $" ({tasa * 100:0.##}%)";
            TRow(lbl + ":", FmtMon(tr.Importe));
        }

        foreach (var ret in cfdi.Retenciones)
            TRow($"Ret. {ImpLabel(ret.Impuesto)}:", $"- {FmtMon(ret.Importe)}");

        TRow("TOTAL:", $"{cfdi.Moneda} {FmtMon(cfdi.Total)}", isTotal: true);

        outer.AddCell(left);
        outer.AddCell(new Cell().SetBorder(Border.NO_BORDER).Add(totals));
        doc.Add(outer);

        // Total en letra
        var letras = NumeroALetras(cfdi.Total, cfdi.Moneda);
        if (!string.IsNullOrWhiteSpace(letras))
            doc.Add(new Paragraph($"SON: {letras}")
                .SetFont(fB).SetFontSize(8f).SetFontColor(CDark)
                .SetBorder(new SolidBorder(CBorder, 0.5f))
                .SetPaddingLeft(8f).SetPaddingTop(4f).SetPaddingBottom(4f)
                .SetMarginBottom(8f));
    }

    // ─────────────────────────────────────────────────────────────
    //  COMPLEMENTO DE PAGO  (TipoDeComprobante = "P")
    // ─────────────────────────────────────────────────────────────
    private static void AddComplementoPago(Document doc, CfdiData cfdi, PdfFont fR, PdfFont fB)
    {
        if (cfdi.Pagos.Count == 0)
        {
            doc.Add(new Paragraph("⚠ No se encontraron datos de Pagos en el complemento.")
                .SetFont(fR).SetFontSize(9f).SetFontColor(CDanger));
            return;
        }

        int numPago = 1;
        foreach (var pago in cfdi.Pagos)
        {
            // ── Cabecera del pago ──────────────────────────────
            doc.Add(new Paragraph($"PAGO #{numPago++}")
                .SetFont(fB).SetFontSize(9.5f).SetFontColor(CWhite)
                .SetBackgroundColor(CPagoHdr)
                .SetPaddingLeft(8f).SetPaddingTop(4f).SetPaddingBottom(4f)
                .SetMarginBottom(6f));

            // Datos del pago en tabla de 2 columnas
            var infoTable = new Table(UnitValue.CreatePercentArray(new float[] { 50f, 50f }))
                .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(6f);

            var col1 = new Cell().SetBorder(Border.NO_BORDER).SetPaddingRight(10f);
            col1.Add(DR("Fecha de pago:", pago.FechaPago, fR, fB));
            col1.Add(DR("Forma de pago:", pago.FormaDePagoP, fR, fB));
            col1.Add(DR("Moneda:", pago.MonedaP, fR, fB));
            if (!string.IsNullOrWhiteSpace(pago.TipoCambioP) && pago.TipoCambioP != "1")
                col1.Add(DR("Tipo de cambio:", pago.TipoCambioP, fR, fB));

            var col2 = new Cell().SetBorder(Border.NO_BORDER).SetPaddingLeft(10f);
            col2.Add(DR("Monto pagado:", $"{pago.MonedaP} {FmtMon(pago.Monto)}", fR, fB));
            if (!string.IsNullOrWhiteSpace(pago.NumOperacion))
                col2.Add(DR("Núm. operación:", pago.NumOperacion, fR, fB));
            if (!string.IsNullOrWhiteSpace(pago.NomBancoOrdExt))
                col2.Add(DR("Banco ordenante:", pago.NomBancoOrdExt, fR, fB));
            if (!string.IsNullOrWhiteSpace(pago.CtaOrdenante))
                col2.Add(DR("Cta. ordenante:", pago.CtaOrdenante, fR, fB));
            if (!string.IsNullOrWhiteSpace(pago.CtaBeneficiario))
                col2.Add(DR("Cta. beneficiario:", pago.CtaBeneficiario, fR, fB));

            infoTable.AddCell(col1);
            infoTable.AddCell(col2);
            doc.Add(infoTable);

            // ── Documentos relacionados ────────────────────────
            if (pago.Documentos.Count > 0)
            {
                doc.Add(new Paragraph("Documentos relacionados")
                    .SetFont(fB).SetFontSize(8f).SetFontColor(CGray)
                    .SetMarginBottom(4f));

                var dt = new Table(UnitValue.CreatePercentArray(
                    new float[] { 30f, 5f, 7f, 8f, 14f, 14f, 14f, 8f }))
                    .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(10f);

                foreach (var h in new[] { "UUID CFDI relacionado", "Serie", "Folio",
                    "Parcialidad", "Saldo anterior", "Importe pagado", "Saldo insoluto", "Moneda DR" })
                {
                    TextAlignment a = h is "Saldo anterior" or "Importe pagado" or "Saldo insoluto"
                        ? TextAlignment.RIGHT
                        : (h is "Serie" or "Folio" or "Parcialidad" ? TextAlignment.CENTER : TextAlignment.LEFT);
                    dt.AddHeaderCell(HdrCell(h, a, fB, CPagoHdr));
                }

                bool alt2 = false;
                foreach (var d in pago.Documentos)
                {
                    var bg = alt2 ? CRowAlt : CWhite; alt2 = !alt2;
                    dt.AddCell(DCell(d.IdDocumento.ToUpperInvariant(), bg, fR, TextAlignment.LEFT, 5.5f));
                    dt.AddCell(DCell(d.Serie, bg, fR, TextAlignment.CENTER));
                    dt.AddCell(DCell(d.Folio, bg, fR, TextAlignment.CENTER));
                    dt.AddCell(DCell(d.NumParcialidad, bg, fR, TextAlignment.CENTER));
                    dt.AddCell(DCell(FmtMon(d.ImpSaldoAnt), bg, fR, TextAlignment.RIGHT));
                    dt.AddCell(DCell(FmtMon(d.ImpPagado), bg, fR, TextAlignment.RIGHT));
                    dt.AddCell(DCell(FmtMon(d.ImpSaldoInsoluto), bg, fR, TextAlignment.RIGHT));
                    dt.AddCell(DCell(d.MonedaDR, bg, fR, TextAlignment.CENTER));
                }
                doc.Add(dt);
            }
        }

        // ── Totales del complemento ────────────────────────────
        if (!string.IsNullOrWhiteSpace(cfdi.PagosTotalesMontoTotal)
            || !string.IsNullOrWhiteSpace(cfdi.PagosTotalesIva16))
        {
            doc.Add(STitle("TOTALES DEL COMPLEMENTO DE PAGO", fB));

            var tt = new Table(UnitValue.CreatePercentArray(new float[] { 60f, 40f }))
                .UseAllAvailableWidth()
                .SetBorder(new SolidBorder(CBorder, 0.5f))
                .SetMarginBottom(12f);

            void PT(string lbl, string val, bool isT = false)
            {
                var bg = isT ? CVigma : CWhite;
                var fc = isT ? CWhite : CDark;
                var fn = isT ? fB : fR;
                float fs = isT ? 10f : 8.5f;
                float pv = isT ? 7f : 4f;

                tt.AddCell(new Cell()
                    .SetBackgroundColor(bg).SetBorder(Border.NO_BORDER)
                    .SetPaddingTop(pv).SetPaddingBottom(pv).SetPaddingLeft(10f)
                    .Add(new Paragraph(lbl).SetFont(fn).SetFontSize(fs).SetFontColor(fc)));
                tt.AddCell(new Cell()
                    .SetBackgroundColor(bg).SetBorder(Border.NO_BORDER)
                    .SetPaddingTop(pv).SetPaddingBottom(pv).SetPaddingRight(10f)
                    .Add(new Paragraph(val).SetFont(fn).SetFontSize(fs).SetFontColor(fc)
                        .SetTextAlignment(TextAlignment.RIGHT)));
            }

            if (!string.IsNullOrWhiteSpace(cfdi.PagosTotalesBaseIva16))
                PT("Base IVA 16%:", FmtMon(cfdi.PagosTotalesBaseIva16));
            if (!string.IsNullOrWhiteSpace(cfdi.PagosTotalesIva16))
                PT("IVA 16% (traslado):", FmtMon(cfdi.PagosTotalesIva16));
            if (!string.IsNullOrWhiteSpace(cfdi.PagosTotalesMontoTotal))
                PT("MONTO TOTAL PAGOS:", FmtMon(cfdi.PagosTotalesMontoTotal), isT: true);

            doc.Add(tt);

            // Total en letra (usa la moneda del primer pago o MXN por defecto)
            var monedaPago = cfdi.Pagos.Count > 0 ? cfdi.Pagos[0].MonedaP : "MXN";
            var letrasP = NumeroALetras(cfdi.PagosTotalesMontoTotal, monedaPago);
            if (!string.IsNullOrWhiteSpace(letrasP))
                doc.Add(new Paragraph($"SON: {letrasP}")
                    .SetFont(fB).SetFontSize(8f).SetFontColor(CDark)
                    .SetBorder(new SolidBorder(CBorder, 0.5f))
                    .SetPaddingLeft(8f).SetPaddingTop(4f).SetPaddingBottom(4f)
                    .SetMarginBottom(8f));
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  TIMBRE FISCAL DIGITAL  (compartido)
    // ─────────────────────────────────────────────────────────────
    private static void AddTimbre(Document doc, CfdiData cfdi, PdfFont fR, PdfFont fB)
    {
        doc.Add(STitle("TIMBRE FISCAL DIGITAL", fB));

        var t = new Table(UnitValue.CreatePercentArray(new float[] { 50f, 50f }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(8f);

        var left = new Cell().SetBorder(Border.NO_BORDER).SetPaddingRight(10f);
        left.Add(DR("UUID:", cfdi.TimbreUuid.ToUpperInvariant(), fR, fB));
        left.Add(DR("Fecha timbrado:", cfdi.TimbreFechaTimbrado, fR, fB));
        left.Add(DR("RFC PAC (certif.):", cfdi.TimbreRfcPac, fR, fB));
        left.Add(DR("No. cert. SAT:", cfdi.TimbreNoCertSat, fR, fB));
        left.Add(DR("Versión CFDI / TFD:", $"{cfdi.Version} / {cfdi.TimbreVersion}", fR, fB));

        var right = new Cell().SetBorder(Border.NO_BORDER).SetPaddingLeft(10f);

        void SelloBlock(string titulo, string sello)
        {
            if (string.IsNullOrWhiteSpace(sello)) return;
            right.Add(new Paragraph(titulo)
                .SetFont(fB).SetFontSize(7f).SetFontColor(CGray).SetMarginBottom(1f));
            right.Add(SelloParrafo(sello, fR));
        }
        SelloBlock("Sello SAT:", cfdi.TimbreSelloSat);
        SelloBlock("Sello CFDI:", cfdi.TimbreSelloCfd);

        t.AddCell(left);
        t.AddCell(right);
        doc.Add(t);
    }

    // ─────────────────────────────────────────────────────────────
    //  DATOS ADICIONALES
    // ─────────────────────────────────────────────────────────────
    private static void AddAdicionales(Document doc,
        Dictionary<string, string> ad, PdfFont fR, PdfFont fB)
    {
        doc.Add(new LineSeparator(new SolidLine(0.5f)).SetMarginTop(4f).SetMarginBottom(8f));
        doc.Add(STitle("DATOS ADICIONALES", fB));

        var t = new Table(UnitValue.CreatePercentArray(new float[] { 35f, 65f }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(10f);

        bool alt = false;
        foreach (var (k, v) in ad)
        {
            var bg = alt ? CRowAlt : CWhite; alt = !alt;
            var b = new SolidBorder(CBorder, 0.3f);

            t.AddCell(new Cell()
                .SetBackgroundColor(bg)
                .SetBorderTop(b).SetBorderBottom(b)
                .SetBorderLeft(Border.NO_BORDER).SetBorderRight(Border.NO_BORDER)
                .SetPaddingTop(4f).SetPaddingBottom(4f).SetPaddingLeft(8f)
                .Add(new Paragraph(k).SetFont(fB).SetFontSize(8f).SetFontColor(CGray)));

            t.AddCell(new Cell()
                .SetBackgroundColor(bg)
                .SetBorderTop(b).SetBorderBottom(b)
                .SetBorderLeft(Border.NO_BORDER).SetBorderRight(Border.NO_BORDER)
                .SetPaddingTop(4f).SetPaddingBottom(4f).SetPaddingLeft(5f)
                .Add(new Paragraph(v ?? "").SetFont(fR).SetFontSize(8f).SetFontColor(CDark)));
        }
        doc.Add(t);
    }

    // ─────────────────────────────────────────────────────────────
    //  PIE DE PÁGINA
    // ─────────────────────────────────────────────────────────────
    private static void AddFooter(Document doc, PdfFont fR)
    {
        doc.Add(new LineSeparator(new SolidLine(0.5f)).SetMarginTop(6f).SetMarginBottom(5f));
        doc.Add(new Paragraph(
            "Verifica la autenticidad de este CFDI en: https://verificacfdi.facturaelectronica.sat.gob.mx")
            .SetFont(fR).SetFontSize(7f).SetFontColor(CGray)
            .SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(2f));
        doc.Add(new Paragraph(
            $"Representación impresa de un CFDI · Vigma Timbrado Gateway · {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
            .SetFont(fR).SetFontSize(6.5f).SetFontColor(CGray)
            .SetTextAlignment(TextAlignment.CENTER));
    }

    // ─────────────────────────────────────────────────────────────
    //  HELPERS UI
    // ─────────────────────────────────────────────────────────────
    private static Paragraph STitle(string txt, PdfFont fB) =>
        new Paragraph(txt)
            .SetFont(fB).SetFontSize(9f).SetFontColor(CVigma)
            .SetBackgroundColor(CLightBg)
            .SetPaddingLeft(8f).SetPaddingTop(4f).SetPaddingBottom(4f)
            .SetMarginBottom(8f);

    private static Paragraph SLabel(string txt, PdfFont fB) =>
        new Paragraph(txt)
            .SetFont(fB).SetFontSize(8f).SetFontColor(CVigma)
            .SetMarginBottom(6f);

    // DR = DataRow
    private static Paragraph DR(string lbl, string? val, PdfFont fR, PdfFont fB)
    {
        var p = new Paragraph().SetMarginBottom(3f);
        p.Add(new Text(lbl + " ").SetFont(fB).SetFontSize(7.5f).SetFontColor(CGray));
        p.Add(new Text(val ?? "—").SetFont(fR).SetFontSize(7.5f).SetFontColor(CDark));
        return p;
    }

    private static Cell HdrCell(string txt, TextAlignment align, PdfFont fB,
        DeviceRgb? bg = null) =>
        new Cell()
            .SetBackgroundColor(bg ?? CTableHdr)
            .SetBorder(Border.NO_BORDER)
            .SetPaddingTop(5f).SetPaddingBottom(5f)
            .SetPaddingLeft(4f).SetPaddingRight(4f)
            .Add(new Paragraph(txt)
                .SetFont(fB).SetFontSize(7.5f).SetFontColor(CWhite)
                .SetTextAlignment(align));

    private static Cell DCell(string txt, DeviceRgb bg, PdfFont fR,
        TextAlignment align = TextAlignment.LEFT, float fs = 7.5f)
    {
        var b = new SolidBorder(CBorder, 0.3f);
        return new Cell()
            .SetBackgroundColor(bg)
            .SetBorderTop(b).SetBorderBottom(b)
            .SetBorderLeft(Border.NO_BORDER).SetBorderRight(Border.NO_BORDER)
            .SetPaddingTop(4f).SetPaddingBottom(4f)
            .SetPaddingLeft(4f).SetPaddingRight(4f)
            .Add(new Paragraph(txt)
                .SetFont(fR).SetFontSize(fs)
                .SetFontColor(CDark).SetTextAlignment(align));
    }

    private static void AddSep(Document doc) =>
        doc.Add(new LineSeparator(new SolidLine(0.5f)).SetMarginTop(4f).SetMarginBottom(10f));

    /// <summary>
    /// Construye un Paragraph para un sello (SAT/CFDI) dividiendo el string
    /// cada <paramref name="chunkSize"/> caracteres con saltos de línea explícitos,
    /// para evitar que el texto largo desborde el margen del PDF.
    /// </summary>
    private static Paragraph SelloParrafo(string sello, PdfFont fR,
        int chunkSize = 62, int maxChars = 400)
    {
        var display = sello.Length > maxChars ? sello[..maxChars] : sello;
        var p = new Paragraph()
            .SetFont(fR).SetFontSize(5.5f).SetFontColor(CGray).SetMarginBottom(5f);

        for (int i = 0; i < display.Length; i += chunkSize)
        {
            if (i > 0) p.Add(new Text("\n"));
            p.Add(new Text(display.Substring(i, Math.Min(chunkSize, display.Length - i))));
        }
        if (sello.Length > maxChars) p.Add(new Text("…"));
        return p;
    }

    // ─────────────────────────────────────────────────────────────
    //  LOGO
    // ─────────────────────────────────────────────────────────────
    private string? ResolverLogo(string? relPath)
    {
        if (string.IsNullOrWhiteSpace(relPath)) return null;
        var rel = relPath.TrimStart('/').Replace('/', System.IO.Path.DirectorySeparatorChar);
        var abs = System.IO.Path.Combine(_env.WebRootPath, rel);
        return File.Exists(abs) ? abs : null;
    }

    // ─────────────────────────────────────────────────────────────
    //  PARSEO CFDI 4.0
    // ─────────────────────────────────────────────────────────────
    private static CfdiData ParsearCfdi(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return new CfdiData();
        try
        {
            XNamespace cfdiNs = "http://www.sat.gob.mx/cfd/4";
            XNamespace tfdNs = "http://www.sat.gob.mx/TimbreFiscalDigital";
            XNamespace p20Ns = "http://www.sat.gob.mx/Pagos20";
            XNamespace p10Ns = "http://www.sat.gob.mx/Pagos";

            var xdoc = XDocument.Parse(xml.Trim());
            var comp = xdoc.Root!;

            string A(XElement? el, string attr) => el?.Attribute(attr)?.Value ?? "";

            var emisor = comp.Element(cfdiNs + "Emisor");
            var receptor = comp.Element(cfdiNs + "Receptor");
            var impuestos = comp.Element(cfdiNs + "Impuestos");
            var complemento = comp.Element(cfdiNs + "Complemento");
            var timbre = complemento?.Element(tfdNs + "TimbreFiscalDigital");

            // Pagos 2.0 (o 1.0 como fallback)
            var pagosEl = complemento?.Element(p20Ns + "Pagos")
                       ?? complemento?.Element(p10Ns + "Pagos");

            // ── Conceptos ─────────────────────────────────────
            var conceptos = comp
                .Element(cfdiNs + "Conceptos")?
                .Elements(cfdiNs + "Concepto")
                .Select(c => new CfdiConcepto
                {
                    ClaveProdServ = A(c, "ClaveProdServ"),
                    ClaveUnidad = A(c, "ClaveUnidad"),
                    Unidad = A(c, "Unidad"),
                    Cantidad = A(c, "Cantidad"),
                    Descripcion = A(c, "Descripcion"),
                    ValorUnitario = A(c, "ValorUnitario"),
                    Importe = A(c, "Importe"),
                    Descuento = A(c, "Descuento"),
                }).ToList() ?? new();

            // ── Traslados / Retenciones globales ───────────────
            var traslados = impuestos?
                .Element(cfdiNs + "Traslados")?
                .Elements(cfdiNs + "Traslado")
                .Select(t => new CfdiTraslado
                {
                    Impuesto = A(t, "Impuesto"),
                    TipoFactor = A(t, "TipoFactor"),
                    TasaOCuota = A(t, "TasaOCuota"),
                    Importe = A(t, "Importe"),
                }).ToList() ?? new();

            var retenciones = impuestos?
                .Element(cfdiNs + "Retenciones")?
                .Elements(cfdiNs + "Retencion")
                .Select(r => new CfdiRetencion
                {
                    Impuesto = A(r, "Impuesto"),
                    Importe = A(r, "Importe"),
                }).ToList() ?? new();

            // ── Complemento de Pago ───────────────────────────
            var pagos = new List<CfdiPago>();
            string pagosTotalesMonto = "", pagosTotalesBase16 = "", pagosTotalesIva16 = "";

            if (pagosEl != null)
            {
                // Detectar namespace del elemento hijo (puede ser p20Ns o p10Ns)
                XNamespace pNs = pagosEl.Name.Namespace;

                // Totales (solo en Pagos20)
                var totalesEl = pagosEl.Element(pNs + "Totales");
                pagosTotalesMonto = A(totalesEl, "MontoTotalPagos");
                pagosTotalesBase16 = A(totalesEl, "TotalTrasladosBaseIVA16");
                pagosTotalesIva16 = A(totalesEl, "TotalTrasladosImpuestoIVA16");

                foreach (var pagoEl in pagosEl.Elements(pNs + "Pago"))
                {
                    var docs = pagoEl.Elements(pNs + "DoctoRelacionado")
                        .Select(d => new CfdiDoctoRelacionado
                        {
                            IdDocumento = A(d, "IdDocumento"),
                            Serie = A(d, "Serie"),
                            Folio = A(d, "Folio"),
                            MonedaDR = A(d, "MonedaDR"),
                            EquivalenciaDR = A(d, "EquivalenciaDR"),
                            NumParcialidad = A(d, "NumParcialidad"),
                            ImpSaldoAnt = A(d, "ImpSaldoAnt"),
                            ImpPagado = A(d, "ImpPagado"),
                            ImpSaldoInsoluto = A(d, "ImpSaldoInsoluto")
                        }).ToList();

                    pagos.Add(new CfdiPago
                    {
                        FechaPago = A(pagoEl, "FechaPago"),
                        FormaDePagoP = A(pagoEl, "FormaDePagoP"),
                        MonedaP = A(pagoEl, "MonedaP"),
                        TipoCambioP = A(pagoEl, "TipoCambioP"),
                        Monto = A(pagoEl, "Monto"),
                        NumOperacion = A(pagoEl, "NumOperacion"),
                        NomBancoOrdExt = A(pagoEl, "NomBancoOrdExt"),
                        CtaOrdenante = A(pagoEl, "CtaOrdenante"),
                        CtaBeneficiario = A(pagoEl, "CtaBeneficiario"),
                        Documentos = docs,
                    });
                }
            }

            return new CfdiData
            {
                Version = A(comp, "Version"),
                Fecha = A(comp, "Fecha"),
                SubTotal = A(comp, "SubTotal"),
                Descuento = A(comp, "Descuento"),
                Total = A(comp, "Total"),
                Moneda = A(comp, "Moneda"),
                TipoCambio = A(comp, "TipoCambio"),
                TipoDeComprobante = A(comp, "TipoDeComprobante"),
                FormaPago = A(comp, "FormaPago"),
                MetodoPago = A(comp, "MetodoPago"),
                LugarExpedicion = A(comp, "LugarExpedicion"),
                Serie = A(comp, "Serie"),
                Folio = A(comp, "Folio"),
                Exportacion = A(comp, "Exportacion"),
                Condiciones = A(comp, "CondicionesDePago"),
                NoCertificado = A(comp, "NoCertificado"),

                EmisorRfc = A(emisor, "Rfc"),
                EmisorNombre = A(emisor, "Nombre"),
                EmisorRegimenFiscal = A(emisor, "RegimenFiscal"),

                ReceptorRfc = A(receptor, "Rfc"),
                ReceptorNombre = A(receptor, "Nombre"),
                ReceptorDomicilioFiscal = A(receptor, "DomicilioFiscalReceptor"),
                ReceptorRegimenFiscal = A(receptor, "RegimenFiscalReceptor"),
                ReceptorUsoCfdi = A(receptor, "UsoCFDI"),

                Conceptos = conceptos,
                Traslados = traslados,
                Retenciones = retenciones,

                TotalImpTrasladados = A(impuestos, "TotalImpuestosTrasladados"),
                TotalImpRetenidos = A(impuestos, "TotalImpuestosRetenidos"),

                // Complemento de Pago
                Pagos = pagos,
                PagosTotalesMontoTotal = pagosTotalesMonto,
                PagosTotalesBaseIva16 = pagosTotalesBase16,
                PagosTotalesIva16 = pagosTotalesIva16,

                TimbreUuid = A(timbre, "UUID"),
                TimbreFechaTimbrado = A(timbre, "FechaTimbrado"),
                TimbreRfcPac = A(timbre, "RfcProvCertif"),
                TimbreNoCertSat = A(timbre, "NoCertificadoSAT"),
                TimbreSelloSat = A(timbre, "SelloSAT"),
                TimbreSelloCfd = A(timbre, "SelloCFD"),
                TimbreVersion = A(timbre, "Version"),
            };
        }
        catch { return new CfdiData(); }
    }

    // ─────────────────────────────────────────────────────────────
    //  PARSEO ADICIONALES JSON
    // ─────────────────────────────────────────────────────────────
    private static Dictionary<string, string> ParsearAdicionales(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var dict = new Dictionary<string, string>();
            var token = JToken.Parse(json);

            void Flatten(JObject obj)
            {
                foreach (var p in obj.Properties())
                    dict[p.Name] = p.Value is JObject nested
                        ? p.Value.ToString()    // deja objetos nested como string
                        : p.Value?.ToString() ?? "";
            }

            if (token is JObject o) Flatten(o);
            else if (token is JArray arr)
            {
                int i = 0;
                foreach (var item in arr)
                {
                    if (item is JObject io) Flatten(io);
                    else dict[$"item_{i++}"] = item?.ToString() ?? "";
                }
            }
            return dict;
        }
        catch { return new Dictionary<string, string> { ["_raw_"] = json }; }
    }

    // ─────────────────────────────────────────────────────────────
    //  FORMATEO
    // ─────────────────────────────────────────────────────────────
    private static string FmtMon(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "0.00";
        return decimal.TryParse(v, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)
            : v;
    }

    private static string FmtDec(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "0";
        return decimal.TryParse(v, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d.ToString("G29", System.Globalization.CultureInfo.InvariantCulture)
            : v;
    }

    private static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..max];

    private static string TipoLabel(string t) => t switch
    {
        "I" => "FACTURA — INGRESO",
        "E" => "NOTA DE CRÉDITO — EGRESO",
        "T" => "COMPROBANTE DE TRASLADO",
        "N" => "COMPROBANTE DE NÓMINA",
        "P" => "COMPLEMENTO DE PAGO",
        _ => "COMPROBANTE FISCAL"
    };

    private static string ImpLabel(string c) => c switch
    {
        "001" => "ISR",
        "002" => "IVA",
        "003" => "IEPS",
        _ => c
    };

    // ─────────────────────────────────────────────────────────────
    //  NÚMERO A LETRAS  (español mexicano)
    // ─────────────────────────────────────────────────────────────
    private static string NumeroALetras(string? montoStr, string moneda = "MXN")
    {
        if (!decimal.TryParse(montoStr,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var monto))
            return "";
        monto = Math.Abs(monto);
        var entero   = (long)Math.Truncate(monto);
        var centavos = (int)Math.Round((monto - Math.Truncate(monto)) * 100);
        var monedaWord = moneda == "USD" ? "DÓLARES" : "PESOS";
        var letras = entero == 0 ? "CERO" : EnteroALetras(entero);
        var sufijo = moneda == "MXN" ? "M.N." : moneda;
        return $"{letras} {monedaWord} {centavos:00}/100 {sufijo}";
    }

    private static string EnteroALetras(long n)
    {
        if (n == 0) return "";
        if (n < 0)  return "MENOS " + EnteroALetras(-n);

        var partes = new System.Text.StringBuilder();

        if (n >= 1_000_000_000)
        {
            long miles = n / 1_000_000_000;
            partes.Append(miles == 1 ? "MIL MILLONES" : EnteroALetras(miles) + " MIL MILLONES");
            n %= 1_000_000_000;
            if (n > 0) partes.Append(' ');
        }

        if (n >= 1_000_000)
        {
            long mills = n / 1_000_000;
            partes.Append(mills == 1 ? "UN MILLÓN" : EnteroALetras(mills) + " MILLONES");
            n %= 1_000_000;
            if (n > 0) partes.Append(' ');
        }

        if (n >= 1_000)
        {
            long miles = n / 1_000;
            partes.Append(miles == 1 ? "MIL" : EnteroALetras(miles) + " MIL");
            n %= 1_000;
            if (n > 0) partes.Append(' ');
        }

        if (n >= 100)
        {
            partes.Append(Centena((int)(n / 100), n % 100 != 0));
            n %= 100;
            if (n > 0) partes.Append(' ');
        }

        if (n > 0)
            partes.Append(MenorDeCien((int)n));

        return partes.ToString().Trim();
    }

    private static string Centena(int c, bool hayMas) => c switch
    {
        1 => hayMas ? "CIENTO" : "CIEN",
        2 => "DOSCIENTOS",
        3 => "TRESCIENTOS",
        4 => "CUATROCIENTOS",
        5 => "QUINIENTOS",
        6 => "SEISCIENTOS",
        7 => "SETECIENTOS",
        8 => "OCHOCIENTOS",
        9 => "NOVECIENTOS",
        _ => ""
    };

    private static string MenorDeCien(int n)
    {
        if (n <= 19) return UnidadODecena(n);
        int dec = n / 10;
        int uni = n % 10;
        // Casos especiales 20-29: "VEINTIUNO", "VEINTIDÓS", ...
        if (dec == 2 && uni > 0)
        {
            string[] v2 = ["", "VEINTIUNO", "VEINTIDÓS", "VEINTITRÉS",
                           "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS",
                           "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"];
            return v2[uni];
        }
        string decStr = dec switch
        {
            2 => "VEINTE",
            3 => "TREINTA",
            4 => "CUARENTA",
            5 => "CINCUENTA",
            6 => "SESENTA",
            7 => "SETENTA",
            8 => "OCHENTA",
            9 => "NOVENTA",
            _ => ""
        };
        return uni == 0 ? decStr : decStr + " Y " + UnidadODecena(uni);
    }

    private static string UnidadODecena(int n) => n switch
    {
        0  => "CERO",
        1  => "UNO",
        2  => "DOS",
        3  => "TRES",
        4  => "CUATRO",
        5  => "CINCO",
        6  => "SEIS",
        7  => "SIETE",
        8  => "OCHO",
        9  => "NUEVE",
        10 => "DIEZ",
        11 => "ONCE",
        12 => "DOCE",
        13 => "TRECE",
        14 => "CATORCE",
        15 => "QUINCE",
        16 => "DIECISÉIS",
        17 => "DIECISIETE",
        18 => "DIECIOCHO",
        19 => "DIECINUEVE",
        _  => ""
    };

    // ─────────────────────────────────────────────────────────────
    //  MODELOS INTERNOS
    // ─────────────────────────────────────────────────────────────
    private sealed class CfdiData
    {
        public string Version { get; init; } = "4.0";
        public string Fecha { get; init; } = "";
        public string SubTotal { get; init; } = "";
        public string Descuento { get; init; } = "";
        public string Total { get; init; } = "";
        public string Moneda { get; init; } = "MXN";
        public string TipoCambio { get; init; } = "";
        public string TipoDeComprobante { get; init; } = "I";
        public string FormaPago { get; init; } = "";
        public string MetodoPago { get; init; } = "";
        public string LugarExpedicion { get; init; } = "";
        public string Serie { get; init; } = "";
        public string Folio { get; init; } = "";
        public string Exportacion { get; init; } = "";
        public string Condiciones { get; init; } = "";
        public string NoCertificado { get; init; } = "";

        public string EmisorRfc { get; init; } = "";
        public string EmisorNombre { get; init; } = "";
        public string EmisorRegimenFiscal { get; init; } = "";

        public string ReceptorRfc { get; init; } = "";
        public string ReceptorNombre { get; init; } = "";
        public string ReceptorDomicilioFiscal { get; init; } = "";
        public string ReceptorRegimenFiscal { get; init; } = "";
        public string ReceptorUsoCfdi { get; init; } = "";

        public List<CfdiConcepto> Conceptos { get; init; } = new();
        public List<CfdiTraslado> Traslados { get; init; } = new();
        public List<CfdiRetencion> Retenciones { get; init; } = new();

        public string TotalImpTrasladados { get; init; } = "";
        public string TotalImpRetenidos { get; init; } = "";

        // Complemento de Pago
        public List<CfdiPago> Pagos { get; init; } = new();
        public string PagosTotalesMontoTotal { get; init; } = "";
        public string PagosTotalesBaseIva16 { get; init; } = "";
        public string PagosTotalesIva16 { get; init; } = "";

        public string TimbreUuid { get; init; } = "";
        public string TimbreFechaTimbrado { get; init; } = "";
        public string TimbreRfcPac { get; init; } = "";
        public string TimbreNoCertSat { get; init; } = "";
        public string TimbreSelloSat { get; init; } = "";
        public string TimbreSelloCfd { get; init; } = "";
        public string TimbreVersion { get; init; } = "";
    }

    private sealed class CfdiConcepto
    {
        public string ClaveProdServ { get; init; } = "";
        public string ClaveUnidad { get; init; } = "";
        public string Unidad { get; init; } = "";
        public string Cantidad { get; init; } = "";
        public string Descripcion { get; init; } = "";
        public string ValorUnitario { get; init; } = "";
        public string Importe { get; init; } = "";
        public string Descuento { get; init; } = "";
    }

    private sealed class CfdiTraslado
    {
        public string Impuesto { get; init; } = "";
        public string TipoFactor { get; init; } = "";
        public string TasaOCuota { get; init; } = "";
        public string Importe { get; init; } = "";
    }

    private sealed class CfdiRetencion
    {
        public string Impuesto { get; init; } = "";
        public string Importe { get; init; } = "";
    }

    private sealed class CfdiPago
    {
        public string FechaPago { get; init; } = "";
        public string FormaDePagoP { get; init; } = "";
        public string MonedaP { get; init; } = "MXN";
        public string TipoCambioP { get; init; } = "";
        public string Monto { get; init; } = "";
        public string NumOperacion { get; init; } = "";
        public string NomBancoOrdExt { get; init; } = "";
        public string CtaOrdenante { get; init; } = "";
        public string CtaBeneficiario { get; init; } = "";
        public List<CfdiDoctoRelacionado> Documentos { get; init; } = new();
    }

    private sealed class CfdiDoctoRelacionado
    {
        public string IdDocumento { get; init; } = "";
        public string Serie { get; init; } = "";
        public string Folio { get; init; } = "";
        public string MonedaDR { get; init; } = "";
        public string EquivalenciaDR { get; init; } = "";
        public string NumParcialidad { get; init; } = "";
        public string ImpSaldoAnt { get; init; } = "";
        public string ImpPagado { get; init; } = "";
        public string ImpSaldoInsoluto { get; init; } = "";

    }
}                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                