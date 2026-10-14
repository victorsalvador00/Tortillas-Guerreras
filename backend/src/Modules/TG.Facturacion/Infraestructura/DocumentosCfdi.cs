using System.Globalization;
using System.Text;
using System.Xml.Linq;
using QRCoder;
using TG.Facturacion.Publico;

namespace TG.Facturacion.Infraestructura;

/// <summary>XML con la estructura de un CFDI 4.0 y complemento de timbre (sello y certificado simulados).</summary>
public static class XmlCfdi
{
    private static readonly XNamespace Cfdi = "http://www.sat.gob.mx/cfd/4";
    private static readonly XNamespace Tfd = "http://www.sat.gob.mx/TimbreFiscalDigital";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private static string M(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string T(decimal v) => v.ToString("0.000000", CultureInfo.InvariantCulture);

    public static byte[] Generar(FacturaParaTimbrar f, Guid uuid, DateTimeOffset fechaTimbrado)
    {
        var tipoComprobante = f.Tipo == "NotaCredito" ? "E" : "I";
        var conceptos = new XElement(Cfdi + "Conceptos",
            f.Conceptos.Select(c =>
            {
                var traslados = new XElement(Cfdi + "Traslados");
                if (c.TasaIeps is > 0)
                {
                    traslados.Add(new XElement(Cfdi + "Traslado", new XAttribute("Base", M(c.Importe)), new XAttribute("Impuesto", "003"),
                        new XAttribute("TipoFactor", "Tasa"), new XAttribute("TasaOCuota", T(c.TasaIeps.Value)), new XAttribute("Importe", M(c.Ieps))));
                }

                traslados.Add(new XElement(Cfdi + "Traslado", new XAttribute("Base", M(c.Importe + c.Ieps)), new XAttribute("Impuesto", "002"),
                    new XAttribute("TipoFactor", "Tasa"), new XAttribute("TasaOCuota", T(c.TasaIva)), new XAttribute("Importe", M(c.Iva))));
                var concepto = new XElement(Cfdi + "Concepto",
                    new XAttribute("ClaveProdServ", c.ClaveProdServ),
                    c.NoIdentificacion is null ? null : new XAttribute("NoIdentificacion", c.NoIdentificacion),
                    new XAttribute("Cantidad", c.Cantidad.ToString("0.####", CultureInfo.InvariantCulture)),
                    new XAttribute("ClaveUnidad", c.ClaveUnidad),
                    new XAttribute("Descripcion", c.Descripcion),
                    new XAttribute("ValorUnitario", c.ValorUnitario.ToString("0.00####", CultureInfo.InvariantCulture)),
                    new XAttribute("Importe", M(c.Importe)),
                    new XAttribute("ObjetoImp", c.ObjetoImp));
                if (c.ObjetoImp == "02")
                {
                    concepto.Add(new XElement(Cfdi + "Impuestos", traslados));
                }

                return concepto;
            }));

        var trasladosTotales = new XElement(Cfdi + "Traslados");
        foreach (var grupo in f.Conceptos.Where(c => c.ObjetoImp == "02").GroupBy(c => c.TasaIva))
        {
            trasladosTotales.Add(new XElement(Cfdi + "Traslado", new XAttribute("Base", M(grupo.Sum(c => c.Importe + c.Ieps))),
                new XAttribute("Impuesto", "002"), new XAttribute("TipoFactor", "Tasa"), new XAttribute("TasaOCuota", T(grupo.Key)),
                new XAttribute("Importe", M(grupo.Sum(c => c.Iva)))));
        }

        foreach (var grupo in f.Conceptos.Where(c => c.TasaIeps is > 0).GroupBy(c => c.TasaIeps!.Value))
        {
            trasladosTotales.Add(new XElement(Cfdi + "Traslado", new XAttribute("Base", M(grupo.Sum(c => c.Importe))),
                new XAttribute("Impuesto", "003"), new XAttribute("TipoFactor", "Tasa"), new XAttribute("TasaOCuota", T(grupo.Key)),
                new XAttribute("Importe", M(grupo.Sum(c => c.Ieps)))));
        }

        var comprobante = new XElement(Cfdi + "Comprobante",
            new XAttribute(XNamespace.Xmlns + "cfdi", Cfdi),
            new XAttribute(XNamespace.Xmlns + "tfd", Tfd),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(Xsi + "schemaLocation", "http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd"),
            new XAttribute("Version", "4.0"),
            new XAttribute("Serie", f.Serie),
            new XAttribute("Folio", f.Folio),
            new XAttribute("Fecha", f.Fecha.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
            new XAttribute("Sello", "SIMULADO"),
            new XAttribute("FormaPago", f.FormaPago),
            new XAttribute("NoCertificado", "00000000000000000000"),
            new XAttribute("Certificado", "SIMULADO"),
            new XAttribute("SubTotal", M(f.Subtotal)),
            new XAttribute("Moneda", "MXN"),
            new XAttribute("Total", M(f.Total)),
            new XAttribute("TipoDeComprobante", tipoComprobante),
            new XAttribute("Exportacion", "01"),
            new XAttribute("MetodoPago", f.MetodoPago),
            new XAttribute("LugarExpedicion", f.Emisor.CodigoPostal),
            f.Periodicidad is null
                ? null
                : new XElement(Cfdi + "InformacionGlobal", new XAttribute("Periodicidad", f.Periodicidad),
                    new XAttribute("Meses", f.Meses ?? string.Empty), new XAttribute("Año", f.Anio ?? string.Empty)),
            new XElement(Cfdi + "Emisor", new XAttribute("Rfc", f.Emisor.Rfc), new XAttribute("Nombre", f.Emisor.RazonSocial),
                new XAttribute("RegimenFiscal", f.Emisor.RegimenFiscal)),
            new XElement(Cfdi + "Receptor", new XAttribute("Rfc", f.Receptor.Rfc), new XAttribute("Nombre", f.Receptor.Nombre),
                new XAttribute("DomicilioFiscalReceptor", f.Receptor.CodigoPostal), new XAttribute("RegimenFiscalReceptor", f.Receptor.RegimenFiscal),
                new XAttribute("UsoCFDI", f.Receptor.UsoCfdi)),
            conceptos,
            trasladosTotales.HasElements
                ? new XElement(Cfdi + "Impuestos", new XAttribute("TotalImpuestosTrasladados", M(f.Iva + f.Ieps)), trasladosTotales)
                : null,
            new XElement(Cfdi + "Complemento",
                new XElement(Tfd + "TimbreFiscalDigital",
                    new XAttribute("Version", "1.1"),
                    new XAttribute("UUID", uuid.ToString().ToUpperInvariant()),
                    new XAttribute("FechaTimbrado", fechaTimbrado.ToOffset(TimeSpan.FromHours(-6)).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                    new XAttribute("RfcProvCertif", "SPR190613I52"),
                    new XAttribute("SelloCFD", "SIMULADO"),
                    new XAttribute("NoCertificadoSAT", "00000000000000000000"),
                    new XAttribute("SelloSAT", "SIMULADO"))));

        var documento = new XDocument(new XDeclaration("1.0", "utf-8", null), comprobante);
        using var flujo = new MemoryStream();
        using (var escritor = System.Xml.XmlWriter.Create(flujo, new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            documento.Save(escritor);
        }

        return flujo.ToArray();
    }
}

/// <summary>
/// Representación impresa en PDF. Se escribe el PDF directamente (fuente estándar Helvetica, sin incrustar) y el QR se
/// dibuja con rectángulos a partir de la matriz de QRCoder: no depende de fuentes instaladas en el servidor.
/// </summary>
public static class PdfCfdi
{
    private const float Ancho = 612;
    private const float Alto = 792;
    private const float Margen = 40;

    private static string M(decimal v) => v.ToString("$#,##0.00", CultureInfo.InvariantCulture);

    public static byte[] Generar(FacturaParaTimbrar f, Guid uuid, DateTimeOffset fechaTimbrado)
    {
        var paginas = new List<StringBuilder>();
        var actual = new StringBuilder();
        paginas.Add(actual);
        var y = Alto - Margen;

        void Texto(float x, float yy, string texto, float tamano = 9, bool negrita = false) =>
            actual.Append(CultureInfo.InvariantCulture,
                $"BT /{(negrita ? "F2" : "F1")} {tamano} Tf {x:0.##} {yy:0.##} Td ({Escapar(texto)}) Tj ET\n");

        void Linea(float yy) => actual.Append(CultureInfo.InvariantCulture, $"0.8 0.8 0.8 RG {Margen} {yy:0.##} m {Ancho - Margen} {yy:0.##} l S\n");

        void NuevaPagina()
        {
            actual = new StringBuilder();
            paginas.Add(actual);
            y = Alto - Margen;
        }

        var titulo = f.Tipo switch { "Global" => "FACTURA GLOBAL", "NotaCredito" => "NOTA DE CRÉDITO", _ => "FACTURA" };
        Texto(Margen, y, $"{titulo} {f.Serie}-{f.Folio}", 16, true);
        Texto(Ancho - 230, y, "CFDI 4.0 · SIMULADO (sin validez fiscal)", 8);
        y -= 22;
        Texto(Margen, y, f.Emisor.RazonSocial, 11, true);
        y -= 13;
        Texto(Margen, y, $"RFC {f.Emisor.Rfc} · Régimen {f.Emisor.RegimenFiscal} · Lugar de expedición {f.Emisor.CodigoPostal}");
        y -= 13;
        Texto(Margen, y, $"Fecha {f.Fecha.ToOffset(TimeSpan.FromHours(-6)):dd/MM/yyyy HH:mm} · Método {f.MetodoPago} · Forma {f.FormaPago}");
        y -= 20;
        Texto(Margen, y, "Receptor", 10, true);
        y -= 13;
        Texto(Margen, y, $"{f.Receptor.Nombre} · RFC {f.Receptor.Rfc}");
        y -= 13;
        Texto(Margen, y, $"Régimen {f.Receptor.RegimenFiscal} · CP {f.Receptor.CodigoPostal} · Uso CFDI {f.Receptor.UsoCfdi}");
        if (f.Periodicidad is not null)
        {
            y -= 13;
            Texto(Margen, y, $"Información global: periodicidad {f.Periodicidad}, meses {f.Meses}, año {f.Anio}");
        }

        y -= 22;
        void Encabezado()
        {
            Texto(Margen, y, "Clave", 8, true);
            Texto(Margen + 60, y, "Cant.", 8, true);
            Texto(Margen + 100, y, "Unidad", 8, true);
            Texto(Margen + 145, y, "Descripción", 8, true);
            Texto(Ancho - 200, y, "Valor unitario", 8, true);
            Texto(Ancho - 110, y, "Importe", 8, true);
            y -= 5;
            Linea(y);
            y -= 12;
        }

        Encabezado();
        foreach (var c in f.Conceptos)
        {
            if (y < 170)
            {
                NuevaPagina();
                Encabezado();
            }

            Texto(Margen, y, c.ClaveProdServ, 8);
            Texto(Margen + 60, y, c.Cantidad.ToString("0.####", CultureInfo.InvariantCulture), 8);
            Texto(Margen + 100, y, c.ClaveUnidad, 8);
            Texto(Margen + 145, y, Recortar(c.Descripcion, 52), 8);
            Texto(Ancho - 200, y, M(c.ValorUnitario), 8);
            Texto(Ancho - 110, y, M(c.Importe), 8);
            y -= 13;
        }

        Linea(y + 6);
        y -= 8;
        Texto(Ancho - 200, y, "Subtotal", 9);
        Texto(Ancho - 110, y, M(f.Subtotal), 9);
        y -= 13;
        if (f.Ieps != 0)
        {
            Texto(Ancho - 200, y, "IEPS", 9);
            Texto(Ancho - 110, y, M(f.Ieps), 9);
            y -= 13;
        }

        Texto(Ancho - 200, y, "IVA", 9);
        Texto(Ancho - 110, y, M(f.Iva), 9);
        y -= 13;
        Texto(Ancho - 200, y, "Total", 10, true);
        Texto(Ancho - 110, y, M(f.Total), 10, true);

        // Pie con timbre y QR de verificación.
        var url = $"https://verificacfdi.facturaelectronica.sat.gob.mx/default.aspx?id={uuid.ToString().ToUpperInvariant()}"
            + $"&re={f.Emisor.Rfc}&rr={f.Receptor.Rfc}&tt={f.Total.ToString("0.000000", CultureInfo.InvariantCulture)}&fe=SIMULA";
        Texto(Margen, 120, $"Folio fiscal (UUID): {uuid.ToString().ToUpperInvariant()}", 8, true);
        Texto(Margen, 108, $"Fecha de timbrado: {fechaTimbrado.ToOffset(TimeSpan.FromHours(-6)):dd/MM/yyyy HH:mm:ss}", 8);
        Texto(Margen, 96, "Este documento es una representación impresa de un CFDI generado por el proveedor SIMULADO.", 7);
        Texto(Margen, 86, "No tiene validez fiscal hasta que se conecte el proveedor de timbrado real (D-06).", 7);
        DibujarQr(actual, url, Ancho - Margen - 90, 40, 90);

        return Ensamblar(paginas);
    }

    private static void DibujarQr(StringBuilder contenido, string texto, float x, float y, float lado)
    {
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.M);
        var matriz = datos.ModuleMatrix;
        var n = matriz.Count;
        var modulo = lado / n;
        contenido.Append("0 0 0 rg\n");
        for (var fila = 0; fila < n; fila++)
        {
            for (var col = 0; col < n; col++)
            {
                if (matriz[fila][col])
                {
                    contenido.Append(CultureInfo.InvariantCulture,
                        $"{x + (col * modulo):0.###} {y + lado - ((fila + 1) * modulo):0.###} {modulo:0.###} {modulo:0.###} re\n");
                }
            }
        }

        contenido.Append("f\n");
    }

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..(maximo - 1)] + "…";

    private static string Escapar(string texto)
    {
        // Helvetica con WinAnsiEncoding: los caracteres fuera de Latin-1 se reemplazan.
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Replace('…', '.').Replace('·', '-'))
        {
            var ch = c > 255 ? '?' : c;
            if (ch is '\\' or '(' or ')')
            {
                sb.Append('\\');
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static byte[] Ensamblar(List<StringBuilder> paginas)
    {
        var objetos = new List<byte[]>();
        var latin1 = Encoding.Latin1;
        // 1 catálogo, 2 páginas, 3 F1, 4 F2, luego por página: página + contenido.
        var hijos = string.Join(" ", paginas.Select((_, i) => $"{5 + (i * 2)} 0 R"));
        objetos.Add(latin1.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        objetos.Add(latin1.GetBytes($"<< /Type /Pages /Kids [{hijos}] /Count {paginas.Count} >>"));
        objetos.Add(latin1.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        objetos.Add(latin1.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
        for (var i = 0; i < paginas.Count; i++)
        {
            var contenido = latin1.GetBytes(paginas[i].ToString());
            objetos.Add(latin1.GetBytes(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Ancho} {Alto}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {6 + (i * 2)} 0 R >>"));
            var flujo = new List<byte>();
            flujo.AddRange(latin1.GetBytes($"<< /Length {contenido.Length} >>\nstream\n"));
            flujo.AddRange(contenido);
            flujo.AddRange(latin1.GetBytes("\nendstream"));
            objetos.Add([.. flujo]);
        }

        using var salida = new MemoryStream();
        void Escribir(string s) => salida.Write(latin1.GetBytes(s));
        Escribir("%PDF-1.4\n%âãÏÓ\n");
        var posiciones = new List<long>();
        for (var i = 0; i < objetos.Count; i++)
        {
            posiciones.Add(salida.Position);
            Escribir($"{i + 1} 0 obj\n");
            salida.Write(objetos[i]);
            Escribir("\nendobj\n");
        }

        var xref = salida.Position;
        Escribir($"xref\n0 {objetos.Count + 1}\n0000000000 65535 f \n");
        foreach (var p in posiciones)
        {
            Escribir(string.Create(CultureInfo.InvariantCulture, $"{p:D10} 00000 n \n"));
        }

        Escribir($"trailer\n<< /Size {objetos.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return salida.ToArray();
    }
}
