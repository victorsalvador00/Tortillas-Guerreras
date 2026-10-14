/** Menú lateral por módulo (07-web); cada opción visible solo con su permiso. */

export interface OpcionMenu {
  etiqueta: string;
  ruta: string;
  icono: string;
  permiso?: string | string[];
}

export interface SeccionMenu {
  etiqueta: string;
  opciones: OpcionMenu[];
}

export const MENU: SeccionMenu[] = [
  {
    etiqueta: 'General',
    opciones: [
      { etiqueta: 'Tablero', ruta: '/', icono: 'space_dashboard', permiso: 'tablero.ver' },
      { etiqueta: 'Reportes', ruta: '/reportes', icono: 'bar_chart', permiso: ['tablero.ver', 'ventas.ver', 'produccion.ver', 'inventario.ver', 'logistica.ver'] },
      { etiqueta: 'Mapa de entregas', ruta: '/mapa', icono: 'map', permiso: ['ventas.ver', 'logistica.ver'] },
    ],
  },
  {
    etiqueta: 'Producción',
    opciones: [
      { etiqueta: 'Plan del día', ruta: '/produccion/plan', icono: 'calendar_view_day', permiso: 'produccion.ver' },
      { etiqueta: 'Órdenes de producción', ruta: '/produccion/ordenes', icono: 'precision_manufacturing', permiso: 'produccion.ver' },
      { etiqueta: 'Recetas', ruta: '/produccion/recetas', icono: 'menu_book', permiso: 'produccion.ver' },
      { etiqueta: 'Líneas', ruta: '/produccion/lineas', icono: 'conveyor_belt', permiso: 'produccion.ver' },
      { etiqueta: 'Variación', ruta: '/produccion/variacion', icono: 'query_stats', permiso: 'produccion.ver' },
    ],
  },
  {
    etiqueta: 'Inventario',
    opciones: [
      { etiqueta: 'Existencias', ruta: '/inventario/existencias', icono: 'inventory', permiso: 'inventario.ver' },
      { etiqueta: 'Kardex', ruta: '/inventario/kardex', icono: 'history', permiso: 'inventario.ver' },
      { etiqueta: 'Ajustes', ruta: '/inventario/ajustes', icono: 'tune', permiso: 'inventario.ajustar' },
      { etiqueta: 'Traspasos', ruta: '/inventario/traspasos', icono: 'sync_alt', permiso: 'inventario.traspasar' },
      { etiqueta: 'Conteos físicos', ruta: '/inventario/conteos', icono: 'fact_check', permiso: 'inventario.contar' },
      { etiqueta: 'Valuación', ruta: '/inventario/valuacion', icono: 'payments', permiso: 'inventario.costos' },
      { etiqueta: 'Almacenes', ruta: '/inventario/almacenes', icono: 'warehouse', permiso: 'inventario.ver' },
    ],
  },
  {
    etiqueta: 'Compras',
    opciones: [
      { etiqueta: 'Órdenes de compra', ruta: '/compras/ordenes', icono: 'receipt_long', permiso: 'compras.ver' },
      { etiqueta: 'Recepciones', ruta: '/compras/recepciones', icono: 'move_to_inbox', permiso: ['compras.recibir', 'compras.ver'] },
      { etiqueta: 'Proveedores', ruta: '/compras/proveedores', icono: 'storefront', permiso: 'compras.ver' },
    ],
  },
  {
    etiqueta: 'Logística',
    opciones: [
      { etiqueta: 'Cargas del día', ruta: '/logistica/cargas', icono: 'local_shipping', permiso: 'logistica.ver' },
      { etiqueta: 'Liquidaciones', ruta: '/logistica/liquidaciones', icono: 'point_of_sale', permiso: 'logistica.ver' },
      { etiqueta: 'Retornos', ruta: '/logistica/retornos', icono: 'assignment_return', permiso: ['logistica.retornos', 'logistica.ver'] },
      { etiqueta: 'Rutas', ruta: '/logistica/rutas', icono: 'route', permiso: 'logistica.configurar' },
      { etiqueta: 'Vehículos', ruta: '/logistica/vehiculos', icono: 'airport_shuttle', permiso: 'logistica.configurar' },
    ],
  },
  {
    etiqueta: 'Ventas',
    opciones: [
      { etiqueta: 'Pedidos', ruta: '/ventas/pedidos', icono: 'shopping_cart', permiso: 'ventas.pedidos' },
      { etiqueta: 'Ventas', ruta: '/ventas/ventas', icono: 'receipt', permiso: 'ventas.ver' },
      { etiqueta: 'Cobros', ruta: '/ventas/cobros', icono: 'request_quote', permiso: 'ventas.cobrar' },
      { etiqueta: 'Incidencias', ruta: '/ventas/incidencias', icono: 'report', permiso: 'ventas.supervisar' },
      { etiqueta: 'Clientes', ruta: '/ventas/clientes', icono: 'groups', permiso: 'ventas.clientes' },
      { etiqueta: 'Listas de precio', ruta: '/ventas/listas-precio', icono: 'sell', permiso: 'ventas.configurar' },
    ],
  },
  {
    etiqueta: 'Facturación',
    opciones: [
      { etiqueta: 'Facturas', ruta: '/facturacion/facturas', icono: 'description', permiso: 'facturacion.ver' },
      { etiqueta: 'Autofactura', ruta: '/facturacion/solicitudes', icono: 'forward_to_inbox', permiso: 'facturacion.ver' },
      { etiqueta: 'Factura global', ruta: '/facturacion/global', icono: 'summarize', permiso: 'facturacion.emitir' },
      { etiqueta: 'Emisor', ruta: '/facturacion/emisor', icono: 'badge', permiso: 'facturacion.configurar' },
    ],
  },
  {
    etiqueta: 'Catálogos',
    opciones: [
      { etiqueta: 'Productos', ruta: '/catalogos/productos', icono: 'inventory_2' },
      { etiqueta: 'Unidades', ruta: '/catalogos/unidades', icono: 'straighten' },
      { etiqueta: 'Motivos de merma', ruta: '/catalogos/motivos-merma', icono: 'delete_sweep' },
      { etiqueta: 'Parámetros', ruta: '/catalogos/parametros', icono: 'settings', permiso: 'catalogos.editar' },
    ],
  },
  {
    etiqueta: 'Seguridad',
    opciones: [
      { etiqueta: 'Usuarios', ruta: '/seguridad/usuarios', icono: 'person', permiso: 'seguridad.usuarios' },
      { etiqueta: 'Roles y permisos', ruta: '/seguridad/roles', icono: 'admin_panel_settings', permiso: 'seguridad.roles' },
      { etiqueta: 'Dispositivos', ruta: '/seguridad/dispositivos', icono: 'smartphone', permiso: 'seguridad.dispositivos' },
    ],
  },
];

/** Etiquetas de las migas de pan por segmento de ruta. */
export function etiquetaRuta(ruta: string): string | undefined {
  for (const seccion of MENU) {
    const opcion = seccion.opciones.find((o) => o.ruta === ruta);
    if (opcion) return opcion.etiqueta;
  }
  return undefined;
}

export function seccionDeRuta(ruta: string): string | undefined {
  return MENU.find((s) => s.opciones.some((o) => o.ruta !== '/' && ruta.startsWith(o.ruta)))?.etiqueta;
}
