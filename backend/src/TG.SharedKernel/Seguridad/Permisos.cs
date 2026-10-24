namespace TG.SharedKernel.Seguridad;

/// <summary>
/// Catálogo de permisos (políticas de autorización, RN-SEG-04). Los roles solo agrupan permisos; la asignación por
/// defecto a roles está en el módulo Seguridad <c>[SUP-01]</c>.
/// </summary>
public static class Permisos
{
    public const string Claim = "permiso";

    public static class Seguridad
    {
        public const string Usuarios = "seguridad.usuarios";
        public const string Roles = "seguridad.roles";
        public const string Dispositivos = "seguridad.dispositivos";
        public const string Bitacora = "seguridad.bitacora";
    }

    public static class Catalogos
    {
        public const string Editar = "catalogos.editar";
    }

    public static class Compras
    {
        public const string Ver = "compras.ver";
        public const string Editar = "compras.editar";
        public const string Autorizar = "compras.autorizar";
        public const string Recibir = "compras.recibir";
        public const string CompraDirecta = "compras.compra_directa";
    }

    public static class Produccion
    {
        public const string Ver = "produccion.ver";
        public const string Recetas = "produccion.recetas";
        public const string Planear = "produccion.planear";
        public const string Operar = "produccion.operar";
        public const string Cerrar = "produccion.cerrar";
    }

    public static class Inventario
    {
        public const string Ver = "inventario.ver";
        public const string Configurar = "inventario.configurar";
        public const string Costos = "inventario.costos";
        public const string Ajustar = "inventario.ajustar";
        public const string Traspasar = "inventario.traspasar";
        public const string Contar = "inventario.contar";
    }

    public static class Logistica
    {
        public const string Configurar = "logistica.configurar";
        public const string Ver = "logistica.ver";
        public const string Cargar = "logistica.cargar";
        public const string Retornos = "logistica.retornos";
        public const string Liquidar = "logistica.liquidar";
        public const string ForzarCierre = "logistica.forzar_cierre";
    }

    public static class Ventas
    {
        public const string Configurar = "ventas.configurar";
        public const string Clientes = "ventas.clientes";
        public const string Supervisar = "ventas.supervisar";
        public const string Pedidos = "ventas.pedidos";
        public const string Ver = "ventas.ver";
        public const string Registrar = "ventas.registrar";
        public const string Cancelar = "ventas.cancelar";
        public const string Cobrar = "ventas.cobrar";
    }

    public static class Facturacion
    {
        public const string Ver = "facturacion.ver";
        public const string Emitir = "facturacion.emitir";
        public const string Cancelar = "facturacion.cancelar";
        public const string Configurar = "facturacion.configurar";
    }

    public static class Sincronizacion
    {
        public const string Operar = "sync.operar";
    }

    public static class Tablero
    {
        public const string Ver = "tablero.ver";
    }

    public static readonly IReadOnlyList<(string Clave, string Descripcion)> Todos =
    [
        (Seguridad.Usuarios, "Administrar usuarios"),
        (Seguridad.Roles, "Administrar roles y permisos"),
        (Seguridad.Dispositivos, "Aprobar y desactivar dispositivos de ruta"),
        (Seguridad.Bitacora, "Consultar la bitácora de cambios"),
        (Catalogos.Editar, "Editar catálogos"),
        (Compras.Ver, "Consultar compras"),
        (Compras.Editar, "Capturar proveedores y órdenes de compra"),
        (Compras.Autorizar, "Autorizar, cancelar y cerrar órdenes de compra"),
        (Compras.Recibir, "Registrar recepciones"),
        (Compras.CompraDirecta, "Registrar compras directas sin orden"),
        (Produccion.Ver, "Consultar producción"),
        (Produccion.Recetas, "Administrar recetas y líneas"),
        (Produccion.Planear, "Planear, liberar y cancelar órdenes de producción"),
        (Produccion.Operar, "Capturar consumos y mermas de producción"),
        (Produccion.Cerrar, "Terminar órdenes de producción"),
        (Inventario.Ver, "Consultar existencias y kardex"),
        (Inventario.Configurar, "Administrar almacenes"),
        (Inventario.Costos, "Consultar costos y valuación"),
        (Inventario.Ajustar, "Registrar ajustes de inventario"),
        (Inventario.Traspasar, "Registrar traspasos"),
        (Inventario.Contar, "Realizar conteos físicos"),
        (Logistica.Configurar, "Administrar rutas y vehículos"),
        (Logistica.Ver, "Consultar logística"),
        (Logistica.Cargar, "Preparar y despachar cargas"),
        (Logistica.Retornos, "Registrar retornos"),
        (Logistica.Liquidar, "Liquidar rutas"),
        (Logistica.ForzarCierre, "Forzar cierre de liquidación con pendientes"),
        (Ventas.Configurar, "Administrar canales y listas de precio"),
        (Ventas.Clientes, "Administrar clientes"),
        (Ventas.Supervisar, "Revisar incidencias y aprobar ubicaciones"),
        (Ventas.Pedidos, "Capturar y asignar pedidos"),
        (Ventas.Ver, "Consultar ventas"),
        (Ventas.Registrar, "Registrar ventas"),
        (Ventas.Cancelar, "Cancelar ventas"),
        (Ventas.Cobrar, "Registrar cobros"),
        (Facturacion.Ver, "Consultar facturas"),
        (Facturacion.Emitir, "Emitir facturas y factura global"),
        (Facturacion.Cancelar, "Cancelar facturas"),
        (Facturacion.Configurar, "Configurar emisor y series"),
        (Sincronizacion.Operar, "Sincronizar la app de ruta"),
        (Tablero.Ver, "Ver el tablero"),
    ];
}
