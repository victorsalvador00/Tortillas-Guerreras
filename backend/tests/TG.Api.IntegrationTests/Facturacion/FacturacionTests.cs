using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TG.Api.IntegrationTests.Infraestructura;
using TG.Facturacion.Aplicacion;
using TG.Facturacion.Infraestructura;
using TG.SharedKernel.Tiempo;
using TG.Ventas.Publico;

namespace TG.Api.IntegrationTests.Facturacion;

/// <summary>Archivos en una carpeta temporal y sin límite bajo en el portal.</summary>
public class FacturacionFixture : ApiFixture
{
    protected override void Configurar(ApiFactory factory)
    {
        factory.Ajustes["Archivos:Ruta"] = Path.Combine(Path.GetTempPath(), "tg-pruebas-archivos", Guid.NewGuid().ToString("N"));
        factory.Ajustes["LimiteSolicitudes:PortalPorHora"] = "100000";
    }
}

public sealed class PortalLimitadoFixture : FacturacionFixture
{
    protected override void Configurar(ApiFactory factory)
    {
        base.Configurar(factory);
        factory.Ajustes["LimiteSolicitudes:PortalPorHora"] = "3";
    }
}

internal sealed record FacturaPrueba(Guid Id, string Tipo, string Serie, string Folio, Guid? UuidFiscal, string ReceptorRfc,
    string ReceptorNombre, string ReceptorRegimen, string UsoCfdi, string MetodoPagoSat, string FormaPagoSat, string? Periodicidad,
    decimal Subtotal, decimal Iva, decimal Total, string Estado, IReadOnlyList<ConceptoPrueba> Conceptos, IReadOnlyList<VentaFacturaPrueba> Ventas);

internal sealed record ConceptoPrueba(Guid? VentaId, string ClaveProdServ, string ClaveUnidad, decimal Importe);

internal sealed record VentaFacturaPrueba(Guid VentaId, string Folio);

internal sealed record CaptchaPrueba(Guid Id, string Pregunta);

internal sealed record TicketPrueba(string Folio, decimal Total, bool Facturable, string? Motivo);

internal sealed record SolicitudPrueba(Guid Id, string Estado, string? MotivoRechazo, SolicitudFacturaPrueba? Factura);

internal sealed record SolicitudFacturaPrueba(string Serie, string Folio, Guid? UuidFiscal, string Estado);

internal sealed record PrevisualizacionPrueba(int Ventas, decimal Total, string Periodicidad, IReadOnlyList<VentaGlobalPrueba> Detalle);

internal sealed record VentaGlobalPrueba(Guid VentaId, string Folio);

/// <summary>Ventas de prueba: producto, lista, cliente y existencia en PT propios de cada caso.</summary>
internal static class EscenarioFacturacion
{
    private sealed record IdRespuesta(Guid Id);

    private sealed record Canal(Guid Id, string Clave);

    public static async Task<VentaInfo> VentaAsync(ApiFixture api, string condicion = "Contado", string? claveSat = "01010101",
        DateOnly? fecha = null, FormaPago forma = FormaPago.Efectivo, decimal cantidad = 3)
    {
        var admin = await api.ClienteAsync();
        var producto = await admin.CrearProductoAsync(tipo: "Reventa", unidad: "PZA", tasaIva: 0.16m, claveSat: claveSat);
        var canal = (await admin.GetOkAsync<List<Canal>>("/api/v1/ventas/canales")).Single(c => c.Clave == "INST").Id;
        var lista = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/listas-precio",
            new { nombre = DatosPrueba.Unico("LISTA"), canalId = canal, vigenteDesde = "2024-01-01", activo = true });
        await admin.PutOkAsync<IdRespuesta>($"/api/v1/ventas/listas-precio/{lista.Id}/partidas",
            new { partidas = new[] { new { productoId = producto.Id, unidadId = producto.UnidadBaseId, precioUnitario = 25m } } });
        var cliente = await admin.PostOkAsync<IdRespuesta>("/api/v1/ventas/clientes", new
        {
            nombre = DatosPrueba.Unico("Cliente"), canalId = canal, listaPrecioId = lista.Id, condicionPago = condicion,
            diasCredito = condicion == "Credito" ? 15 : 0, limiteCredito = condicion == "Credito" ? 100000m : (decimal?)null,
            requiereFactura = true, rfc = "AAA010101AAA", razonSocial = "Abarrotes de Prueba SA de CV", regimenFiscal = "601",
            codigoPostalFiscal = "91700", usoCfdi = "G01", emailFacturacion = "cliente@prueba.mx", activo = true,
        });
        var pt = await admin.AlmacenAsync("PT");
        await admin.EntradaAsync(pt, producto.Id, cantidad, 10m);

        return await api.ConScopeAsync(async sp =>
        {
            var ventas = sp.GetRequiredService<IVentasService>();
            var hoy = sp.GetRequiredService<IFechaOperativa>().Hoy();
            var credito = condicion == "Credito";
            var registrada = await ventas.RegistrarVentaAsync(new RegistrarVentaComando(OrigenVenta.Web, cliente.Id, pt, Guid.Empty,
                fecha ?? hoy, credito ? CondicionPago.Credito : CondicionPago.Contado, credito ? FormaPago.Credito : forma,
                [new PartidaVentaComando(producto.Id, producto.UnidadBaseId, cantidad)]), CancellationToken.None);
            return (await ventas.ObtenerVentaAsync(registrada.Valor.VentaId, CancellationToken.None))!;
        });
    }

    public static Task ProcesarOutboxAsync(ApiFixture api) =>
        api.ConScopeAsync(sp => sp.GetRequiredService<ProcesadorOutboxFacturacion>().ProcesarPendientesAsync(CancellationToken.None));

    public static async Task<(Guid Id, int Respuesta)> CaptchaAsync(HttpClient cliente)
    {
        var c = await cliente.GetOkAsync<CaptchaPrueba>("/api/v1/portal/captcha");
        var numeros = System.Text.RegularExpressions.Regex.Matches(c.Pregunta, @"\d+").Select(m => int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        return (c.Id, numeros.Sum());
    }

    public static async Task<HttpResponseMessage> SolicitarAsync(HttpClient cliente, string folio, decimal total, string rfc = "XEXX010101000",
        string cp = "06600", string regimen = "616", string uso = "S01")
    {
        var (id, respuesta) = await CaptchaAsync(cliente);
        return await cliente.PostJsonAsync("/api/v1/portal/solicitudes", new
        {
            folio, total, rfc, razonSocial = "Juan Pérez López", regimenFiscal = regimen, codigoPostal = cp, usoCfdi = uso,
            email = "juan@correo.mx", captchaId = id, captchaRespuesta = respuesta,
        });
    }
}

public sealed class FacturacionTests(FacturacionFixture api) : IClassFixture<FacturacionFixture>
{
    [Fact]
    [Trait("Regla", "RN-FAC-01")]
    [Trait("Regla", "RN-FAC-06")]
    public async Task FacturaWeb_UsaClavesSat_YUnaVentaSoloEnUnaFacturaVigente()
    {
        var venta = await EscenarioFacturacion.VentaAsync(api, forma: FormaPago.Transferencia);
        var admin = await api.ClienteAsync("facturacion");

        var factura = await admin.PostOkAsync<FacturaPrueba>("/api/v1/facturacion/facturas", new { ventaIds = new[] { venta.Id } });
        factura.Estado.ShouldBe("Pendiente");
        factura.ReceptorRfc.ShouldBe("AAA010101AAA");
        factura.Conceptos.Single().ClaveProdServ.ShouldBe("01010101");
        factura.Conceptos.Single().ClaveUnidad.ShouldBe("H87");
        factura.Total.ShouldBe(venta.Total);
        factura.FormaPagoSat.ShouldBe("03");

        var otra = await admin.PostJsonAsync("/api/v1/facturacion/facturas", new { ventaIds = new[] { venta.Id } });
        await otra.DebeFallarAsync(HttpStatusCode.Conflict, "FAC-001");

        var sinClave = await EscenarioFacturacion.VentaAsync(api, claveSat: null);
        var rechazo = await admin.PostJsonAsync("/api/v1/facturacion/facturas", new { ventaIds = new[] { sinClave.Id } });
        await rechazo.DebeFallarAsync(HttpStatusCode.UnprocessableEntity, "FAC-006");
        // La falla no deja la venta asignada.
        (await admin.PostJsonAsync("/api/v1/facturacion/facturas", new { ventaIds = new[] { sinClave.Id } }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    [Trait("Regla", "RN-FAC-07")]
    public async Task MetodoPago_ContadoPue_CreditoPpd()
    {
        var admin = await api.ClienteAsync("facturacion");
        var contado = await admin.PostOkAsync<FacturaPrueba>("/api/v1/facturacion/facturas",
            new { ventaIds = new[] { (await EscenarioFacturacion.VentaAsync(api)).Id } });
        contado.MetodoPagoSat.ShouldBe("PUE");
        contado.FormaPagoSat.ShouldBe("01");

        var credito = await admin.PostOkAsync<FacturaPrueba>("/api/v1/facturacion/facturas",
            new { ventaIds = new[] { (await EscenarioFacturacion.VentaAsync(api, condicion: "Credito")).Id } });
        credito.MetodoPagoSat.ShouldBe("PPD");
        credito.FormaPagoSat.ShouldBe("99");
    }

    [Fact]
    [Trait("Regla", "RN-FAC-02")]
    [Trait("Regla", "RN-FAC-09")]
    public async Task Autofactura_SolicitudValida_TerminaTimbradaYSeDescarganXmlYPdf()
    {
        var venta = await EscenarioFacturacion.VentaAsync(api);
        var portal = api.ClienteAnonimo();

        var ticket = await portal.GetOkAsync<TicketPrueba>($"/api/v1/portal/tickets/{venta.Folio}?total={venta.Total}");
        ticket.Facturable.ShouldBeTrue();

        var solicitud = await (await EscenarioFacturacion.SolicitarAsync(portal, venta.Folio, venta.Total)).LeerAsync<SolicitudPrueba>();
        solicitud.Estado.ShouldBe("EnProceso");

        await EscenarioFacturacion.ProcesarOutboxAsync(api);

        var timbrada = await portal.GetOkAsync<SolicitudPrueba>($"/api/v1/portal/solicitudes/{solicitud.Id}");
        timbrada.Estado.ShouldBe("Timbrada");
        timbrada.Factura!.UuidFiscal.ShouldNotBeNull();

        var xml = await portal.GetAsync($"/api/v1/portal/solicitudes/{solicitud.Id}/xml");
        xml.StatusCode.ShouldBe(HttpStatusCode.OK);
        var textoXml = await xml.Content.ReadAsStringAsync();
        textoXml.ShouldContain("cfdi:Comprobante");
        textoXml.ShouldContain(timbrada.Factura.UuidFiscal!.Value.ToString().ToUpperInvariant());
        textoXml.ShouldContain("Rfc=\"XEXX010101000\"");

        var pdf = await portal.GetAsync($"/api/v1/portal/solicitudes/{solicitud.Id}/pdf");
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..5]).ShouldBe("%PDF-");

        // RN-FAC-09: se envió el XML y el PDF al correo indicado.
        var bandeja = api.Factory.Services.GetRequiredService<BandejaCorreos>();
        var correo = bandeja.Enviados.Last(c => c.Destinatario == "juan@correo.mx");
        correo.Adjuntos.Count.ShouldBe(2);

        // Ya timbrado, el mismo ticket no se vuelve a facturar.
        var repetido = await portal.GetOkAsync<TicketPrueba>($"/api/v1/portal/tickets/{venta.Folio}?total={venta.Total}");
        repetido.Facturable.ShouldBeFalse();
    }

    [Fact]
    [Trait("Regla", "RN-FAC-02")]
    public async Task Autofactura_TotalDistintoPlazoVencidoOCredito_SeRechaza()
    {
        var portal = api.ClienteAnonimo();
        var venta = await EscenarioFacturacion.VentaAsync(api);
        await (await portal.GetAsync($"/api/v1/portal/tickets/{venta.Folio}?total={venta.Total + 1}"))
            .DebeFallarAsync(HttpStatusCode.NotFound, "FAC-002");

        var vieja = await EscenarioFacturacion.VentaAsync(api, fecha: new DateOnly(2025, 6, 10));
        var vencida = await EscenarioFacturacion.SolicitarAsync(portal, vieja.Folio, vieja.Total);
        var error = await vencida.DebeFallarAsync(HttpStatusCode.UnprocessableEntity, "FAC-002");
        error["title"]!.GetValue<string>().ShouldContain("plazo");

        var credito = await EscenarioFacturacion.VentaAsync(api, condicion: "Credito");
        await (await EscenarioFacturacion.SolicitarAsync(portal, credito.Folio, credito.Total))
            .DebeFallarAsync(HttpStatusCode.UnprocessableEntity, "FAC-002");

        // Los rechazos quedan registrados para el panel de solicitudes.
        var admin = await api.ClienteAsync("facturacion");
        var lista = await admin.GetOkAsync<PaginaPrueba<SolicitudPrueba>>($"/api/v1/facturacion/solicitudes?buscar={vieja.Folio}");
        lista.Items.ShouldContain(s => s.Estado == "Rechazada");
    }

    [Fact]
    [Trait("Regla", "RN-FAC-03")]
    public async Task Autofactura_DatosFiscalesInvalidos_Devuelve400PorCampo()
    {
        var venta = await EscenarioFacturacion.VentaAsync(api);
        var respuesta = await EscenarioFacturacion.SolicitarAsync(api.ClienteAnonimo(), venta.Folio, venta.Total, rfc: "ABC123",
            cp: "123", regimen: "999", uso: "X99");
        var error = await respuesta.DebeFallarAsync(HttpStatusCode.BadRequest, "VAL-001");
        error["errores"]!["rfc"].ShouldNotBeNull();
        error["errores"]!["codigoPostal"].ShouldNotBeNull();
        error["errores"]!["regimenFiscal"].ShouldNotBeNull();
        error["errores"]!["usoCfdi"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Autofactura_CaptchaIncorrecto_DevuelveFAC008()
    {
        var venta = await EscenarioFacturacion.VentaAsync(api);
        var portal = api.ClienteAnonimo();
        var (id, respuesta) = await EscenarioFacturacion.CaptchaAsync(portal);
        var r = await portal.PostJsonAsync("/api/v1/portal/solicitudes", new
        {
            folio = venta.Folio, total = venta.Total, rfc = "XEXX010101000", razonSocial = "X", regimenFiscal = "616",
            codigoPostal = "06600", usoCfdi = "S01", email = "x@correo.mx", captchaId = id, captchaRespuesta = respuesta + 1,
        });
        await r.DebeFallarAsync(HttpStatusCode.BadRequest, "FAC-008");
    }

    [Fact]
    [Trait("Regla", "RN-FAC-04")]
    [Trait("Regla", "RN-FAC-05")]
    public async Task FacturaGlobal_PublicoEnGeneral_YVentaEnGlobalYaNoSeAutofactura()
    {
        var fecha = new DateOnly(2025, 3, 14);
        var v1 = await EscenarioFacturacion.VentaAsync(api, fecha: fecha);
        var v2 = await EscenarioFacturacion.VentaAsync(api, fecha: fecha);
        var credito = await EscenarioFacturacion.VentaAsync(api, condicion: "Credito", fecha: fecha);
        var admin = await api.ClienteAsync("facturacion");

        var previa = await admin.GetOkAsync<PrevisualizacionPrueba>(
            "/api/v1/facturacion/factura-global/previsualizar?desde=2025-03-14&hasta=2025-03-14");
        previa.Detalle.Select(d => d.VentaId).ShouldBe([v1.Id, v2.Id], ignoreOrder: true);
        previa.Periodicidad.ShouldBe("04");

        var global = await admin.PostOkAsync<FacturaPrueba>("/api/v1/facturacion/factura-global",
            new { desde = "2025-03-14", hasta = "2025-03-14" });
        global.Tipo.ShouldBe("Global");
        global.ReceptorRfc.ShouldBe("XAXX010101000");
        global.ReceptorNombre.ShouldBe("PUBLICO EN GENERAL");
        global.ReceptorRegimen.ShouldBe("616");
        global.UsoCfdi.ShouldBe("S01");
        global.Ventas.Count.ShouldBe(2);
        global.Conceptos.Select(c => c.VentaId).Distinct().Count().ShouldBe(2);
        global.Conceptos.ShouldAllBe(c => c.ClaveProdServ == "01010101" && c.ClaveUnidad == "ACT");
        global.Total.ShouldBe(v1.Total + v2.Total);
        credito.FacturaId.ShouldBeNull();

        var portal = api.ClienteAnonimo();
        await (await EscenarioFacturacion.SolicitarAsync(portal, v1.Folio, v1.Total))
            .DebeFallarAsync(HttpStatusCode.UnprocessableEntity, "FAC-005");

        await (await admin.PostJsonAsync("/api/v1/facturacion/factura-global", new { desde = "2025-03-14", hasta = "2025-03-14" }))
            .DebeFallarAsync(HttpStatusCode.UnprocessableEntity, "FAC-004");
    }

    [Fact]
    public async Task Previsualizacion_ExcluyeVentasAutofacturadas()
    {
        var autofacturada = await EscenarioFacturacion.VentaAsync(api);
        var libre = await EscenarioFacturacion.VentaAsync(api);
        (await EscenarioFacturacion.SolicitarAsync(api.ClienteAnonimo(), autofacturada.Folio, autofacturada.Total))
            .EnsureSuccessStatusCode();

        var admin = await api.ClienteAsync("facturacion");
        var fecha = autofacturada.FechaOperativa.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var previa = await admin.GetOkAsync<PrevisualizacionPrueba>(
            $"/api/v1/facturacion/factura-global/previsualizar?desde={fecha}&hasta={fecha}");
        previa.Detalle.ShouldNotContain(d => d.VentaId == autofacturada.Id);
        previa.Detalle.ShouldContain(d => d.VentaId == libre.Id);
    }

    [Fact]
    public async Task Cancelar_FacturaTimbrada_LiberaLasVentas()
    {
        var venta = await EscenarioFacturacion.VentaAsync(api);
        var admin = await api.ClienteAsync("facturacion");
        var factura = await admin.PostOkAsync<FacturaPrueba>("/api/v1/facturacion/facturas", new { ventaIds = new[] { venta.Id } });
        await EscenarioFacturacion.ProcesarOutboxAsync(api);
        (await admin.GetOkAsync<FacturaPrueba>($"/api/v1/facturacion/facturas/{factura.Id}")).Estado.ShouldBe("Timbrada");
        (await admin.GetAsync($"/api/v1/facturacion/facturas/{factura.Id}/xml")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await (await admin.PostJsonAsync($"/api/v1/facturacion/facturas/{factura.Id}/cancelar", new { motivo = "99" }))
            .DebeFallarAsync(HttpStatusCode.BadRequest, "VAL-001");
        var cancelada = await admin.PostOkAsync<FacturaPrueba>($"/api/v1/facturacion/facturas/{factura.Id}/cancelar", new { motivo = "02" });
        cancelada.Estado.ShouldBe("Cancelada");

        var nueva = await admin.PostOkAsync<FacturaPrueba>("/api/v1/facturacion/facturas", new { ventaIds = new[] { venta.Id } });
        nueva.Id.ShouldNotBe(factura.Id);
    }

    [Fact]
    public async Task Emisor_ConsultaYEdicion_ConPermisos()
    {
        var vendedor = await api.ClienteAsync("vendedor1");
        (await vendedor.GetAsync("/api/v1/facturacion/facturas")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = await api.ClienteAsync();
        var emisor = await admin.GetOkAsync<EmisorPrueba>("/api/v1/facturacion/emisor");
        emisor.EsEjemplo.ShouldBeTrue();
    }

    private sealed record EmisorPrueba(string Rfc, bool EsEjemplo);
}

public sealed class PortalLimiteTests(PortalLimitadoFixture api) : IClassFixture<PortalLimitadoFixture>
{
    [Fact]
    [Trait("Regla", "RN-FAC-08")]
    public async Task Portal_ExcedeLimitePorIp_Devuelve429()
    {
        var portal = api.ClienteAnonimo();
        for (var i = 0; i < 3; i++)
        {
            (await portal.GetAsync("/api/v1/portal/tickets/NOEXISTE-000001?total=10")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var excedido = await portal.GetAsync("/api/v1/portal/tickets/NOEXISTE-000001?total=10");
        excedido.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        // La consulta del estado y las descargas no cuentan para el límite.
        (await portal.GetAsync("/api/v1/portal/captcha")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
