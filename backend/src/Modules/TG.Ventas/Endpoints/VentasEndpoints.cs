using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TG.SharedKernel.Api;
using TG.Ventas.Aplicacion;
using TG.Ventas.Publico;
using P = TG.SharedKernel.Seguridad.Permisos.Ventas;

namespace TG.Ventas.Endpoints;

internal static class VentasEndpoints
{
    public static IEndpointRouteBuilder Mapear(IEndpointRouteBuilder api)
    {
        var grupo = api.MapGroup("/ventas").WithTags("Ventas");

        // Canales y listas: lectura para cualquier usuario autenticado (selectores); escritura con ventas.configurar.
        var canales = grupo.MapGroup("/canales");
        canales.MapGet("/", (CanalesHandler h, CancellationToken ct) => h.ListarAsync(ct)).RequireAuthorization().WithName("listarCanales");
        canales.MapPost("/", async (GuardarCanalRequest r, CanalesHandler h, CancellationToken ct) =>
            (await h.GuardarAsync(null, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<GuardarCanalRequest>().WithName("crearCanal");
        canales.MapPut("/{id:guid}", async (Guid id, GuardarCanalRequest r, CanalesHandler h, CancellationToken ct) =>
            (await h.GuardarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<GuardarCanalRequest>().WithName("actualizarCanal");

        var listas = grupo.MapGroup("/listas-precio");
        listas.MapGet("/", (ListasPrecioHandler h, CancellationToken ct) => h.ListarAsync(ct)).RequireAuthorization().WithName("listarListasPrecio");
        listas.MapGet("/{id:guid}", async (Guid id, ListasPrecioHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).RequireAuthorization().WithName("obtenerListaPrecio");
        listas.MapPost("/", async (GuardarListaPrecioRequest r, ListasPrecioHandler h, CancellationToken ct) =>
            (await h.GuardarAsync(null, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<GuardarListaPrecioRequest>().WithName("crearListaPrecio");
        listas.MapPut("/{id:guid}", async (Guid id, GuardarListaPrecioRequest r, ListasPrecioHandler h, CancellationToken ct) =>
            (await h.GuardarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<GuardarListaPrecioRequest>().WithName("actualizarListaPrecio");
        listas.MapPut("/{id:guid}/partidas", async (Guid id, PartidasListaRequest r, ListasPrecioHandler h, CancellationToken ct) =>
            (await h.GuardarPartidasAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<PartidasListaRequest>().WithName("guardarPartidasListaPrecio");

        var clientes = grupo.MapGroup("/clientes");
        clientes.MapGet("/", (string? buscar, Guid? canalId, bool? activo, bool? ubicacionPorAprobar, bool? esGenerico, int? pagina,
                int? tamano, ClientesHandler h, CancellationToken ct) =>
            h.ListarAsync(buscar, canalId, activo, ubicacionPorAprobar, esGenerico, pagina, tamano, ct))
            .RequireAuthorization().WithName("listarClientes");
        clientes.MapGet("/{id:guid}", async (Guid id, ClientesHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).RequireAuthorization().WithName("obtenerCliente");
        clientes.MapGet("/{id:guid}/saldo", async (Guid id, ClientesHandler h, CancellationToken ct) =>
            (await h.SaldoAsync(id, ct)).AHttp()).RequireAuthorization().WithName("obtenerSaldoCliente");
        clientes.MapPost("/", async (GuardarClienteRequest r, ClientesHandler h, CancellationToken ct) =>
            (await h.GuardarAsync(null, r, ct)).AHttp()).RequireAuthorization(P.Clientes).Validar<GuardarClienteRequest>().WithName("crearCliente");
        clientes.MapPut("/{id:guid}", async (Guid id, GuardarClienteRequest r, ClientesHandler h, CancellationToken ct) =>
            (await h.GuardarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Clientes).Validar<GuardarClienteRequest>().WithName("actualizarCliente");
        clientes.MapPost("/{id:guid}/aprobar-ubicacion", async (Guid id, AprobarUbicacionRequest r, ClientesHandler h, CancellationToken ct) =>
            (await h.AprobarUbicacionAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Supervisar).WithName("aprobarUbicacionCliente");

        clientes.MapGet("/{id:guid}/precios", async (Guid id, PreciosClienteHandler h, CancellationToken ct) =>
            (await h.ListarAsync(id, ct)).AHttp()).RequireAuthorization().WithName("listarPreciosCliente");
        clientes.MapPut("/{id:guid}/precios", async (Guid id, PreciosClienteRequest r, PreciosClienteHandler h, CancellationToken ct) =>
            (await h.ReemplazarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<PreciosClienteRequest>()
            .WithName("guardarPreciosCliente");

        var grupos = grupo.MapGroup("/grupos-clientes");
        grupos.MapGet("/", (bool? activo, GruposClientesHandler h, CancellationToken ct) => h.ListarAsync(activo, ct))
            .RequireAuthorization().WithName("listarGruposClientes");
        grupos.MapPost("/", async (GuardarGrupoClienteRequest r, GruposClientesHandler h, CancellationToken ct) =>
            (await h.GuardarAsync(null, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<GuardarGrupoClienteRequest>()
            .WithName("crearGrupoCliente");
        grupos.MapPut("/{id:guid}", async (Guid id, GuardarGrupoClienteRequest r, GruposClientesHandler h, CancellationToken ct) =>
            (await h.GuardarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<GuardarGrupoClienteRequest>()
            .WithName("actualizarGrupoCliente");

        var exclusivos = grupo.MapGroup("/productos/{productoId:guid}/exclusivos");
        exclusivos.MapGet("/", async (Guid productoId, ProductosExclusivosHandler h, CancellationToken ct) =>
            (await h.ListarAsync(productoId, ct)).AHttp()).RequireAuthorization().WithName("listarProductoExclusivos");
        exclusivos.MapPut("/", async (Guid productoId, ProductosExclusivosRequest r, ProductosExclusivosHandler h, CancellationToken ct) =>
            (await h.ReemplazarAsync(productoId, r, ct)).AHttp()).RequireAuthorization(P.Configurar).Validar<ProductosExclusivosRequest>()
            .WithName("guardarProductoExclusivos");

        var pedidos = grupo.MapGroup("/pedidos").RequireAuthorization(P.Pedidos);
        pedidos.MapGet("/", (EstadoPedido? estado, Guid? clienteId, Guid? rutaId, DateOnly? desde, DateOnly? hasta, Guid? envioId,
                bool? sinCarga, int? pagina, int? tamano, OrigenVenta? origen, PedidosHandler h, CancellationToken ct) =>
            h.ListarAsync(estado, clienteId, rutaId, desde, hasta, envioId, sinCarga, pagina, tamano, ct, origen)).WithName("listarPedidos");
        pedidos.MapGet("/{id:guid}", async (Guid id, PedidosHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).WithName("obtenerPedido");
        pedidos.MapPost("/", async (GuardarPedidoRequest r, PedidosHandler h, CancellationToken ct) =>
            (await h.GuardarAsync(null, r, ct)).AHttp()).Validar<GuardarPedidoRequest>().WithName("crearPedido");
        pedidos.MapPut("/{id:guid}", async (Guid id, GuardarPedidoRequest r, PedidosHandler h, CancellationToken ct) =>
            (await h.GuardarAsync(id, r, ct)).AHttp()).Validar<GuardarPedidoRequest>().WithName("actualizarPedido");
        pedidos.MapPost("/{id:guid}/confirmar", async (Guid id, PedidosHandler h, CancellationToken ct) =>
            (await h.ConfirmarAsync(id, ct)).AHttp()).WithName("confirmarPedido");
        pedidos.MapPost("/{id:guid}/asignar", async (Guid id, AsignarPedidoRequest r, PedidosHandler h, CancellationToken ct) =>
            (await h.AsignarAsync(id, r, ct)).AHttp()).WithName("asignarPedido");
        pedidos.MapPost("/{id:guid}/cancelar", async (Guid id, PedidosHandler h, CancellationToken ct) =>
            (await h.CancelarAsync(id, ct)).AHttp()).WithName("cancelarPedido");

        var ventas = grupo.MapGroup("/ventas");
        ventas.MapGet("/", (DateOnly? desde, DateOnly? hasta, Guid? rutaId, Guid? clienteId, Guid? pedidoId, EstadoVenta? estado,
                bool? requiereRevision, OrigenVenta? origen, string? folio, int? pagina, int? tamano, VentasWebHandler h, CancellationToken ct) =>
            h.ListarAsync(desde, hasta, rutaId, clienteId, pedidoId, estado, requiereRevision, origen, folio, pagina, tamano, ct))
            .RequireAuthorization(P.Ver).WithName("listarVentas");
        ventas.MapGet("/{id:guid}", async (Guid id, VentasWebHandler h, CancellationToken ct) =>
            (await h.ObtenerAsync(id, ct)).AHttp()).RequireAuthorization(P.Ver).WithName("obtenerVenta");
        ventas.MapPost("/", async (RegistrarVentaWebRequest r, VentasWebHandler h, CancellationToken ct) =>
            (await h.RegistrarAsync(r, ct)).AHttp()).RequireAuthorization(P.Registrar).Validar<RegistrarVentaWebRequest>().WithName("registrarVenta");
        ventas.MapPost("/{id:guid}/cancelar", async (Guid id, CancelarVentaRequest r, VentasWebHandler h, CancellationToken ct) =>
            (await h.CancelarAsync(id, r, ct)).AHttp()).RequireAuthorization(P.Cancelar).Validar<CancelarVentaRequest>().WithName("cancelarVenta");

        var cobros = grupo.MapGroup("/cobros").RequireAuthorization(P.Cobrar);
        cobros.MapGet("/", (Guid? clienteId, Guid? rutaId, DateOnly? desde, DateOnly? hasta, int? pagina, int? tamano, CobrosHandler h,
            CancellationToken ct) => h.ListarAsync(clienteId, rutaId, desde, hasta, pagina, tamano, ct)).WithName("listarCobros");
        cobros.MapPost("/", async (RegistrarCobroRequest r, CobrosHandler h, CancellationToken ct) =>
            (await h.RegistrarAsync(r, ct)).AHttp()).WithName("registrarCobro");

        var incidencias = grupo.MapGroup("/incidencias").RequireAuthorization(P.Supervisar);
        incidencias.MapGet("/", (EstadoIncidencia? estado, TipoIncidencia? tipo, Guid? entidadId, DateOnly? desde, int? pagina, int? tamano,
            IncidenciasHandler h, CancellationToken ct) => h.ListarAsync(estado, tipo, entidadId, desde, pagina, tamano, ct)).WithName("listarIncidencias");
        incidencias.MapPost("/{id:guid}/revisar", async (Guid id, RevisarIncidenciaRequest r, IncidenciasHandler h, CancellationToken ct) =>
            (await h.RevisarAsync(id, r, ct)).AHttp()).WithName("revisarIncidencia");

        return api;
    }
}
