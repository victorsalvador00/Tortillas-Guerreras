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
      { etiqueta: 'Reportes', ruta: '/reportes', icono: 'analytics', permiso: ['tablero.ver', 'ventas.ver', 'produccion.ver', 'inventario.ver', 'logistica.ver'] },
      { etiqueta: 'Mapa de entregas', ruta: '/mapa', icono: 'pin_drop', permiso: ['ventas.ver', 'logistica.ver'] },
    ],
  },
  {
    etiqueta: 'Producción',
    opciones: [
      { etiqueta: 'Órdenes de producción', ruta: '/produccion/ordenes', icono: 'factory', permiso: 'produccion.ver' },
      { etiqueta: 'Recetas', ruta: '/produccion/recetas', icono: 'cooking', permiso: 'produccion.ver' },
      { etiqueta: 'Líneas', ruta: '/produccion/lineas', icono: 'conveyor_belt', permiso: 'produccion.ver' },
      { etiqueta: 'Variación', ruta: '/produccion/variacion', icono: 'balance', permiso: 'produccion.ver' },
    ],
  },
  {
    etiqueta: 'Inventario',
    opciones: [
      { etiqueta: 'Existencias', ruta: '/inventario/existencias', icono: 'shelves', permiso: 'inventario.ver' },
      { etiqueta: 'Kardex', ruta: '/inventario/kardex', icono: 'swap_vert', permiso: 'inventario.ver' },
      { etiqueta: 'Ajustes', ruta: '/inventario/ajustes', icono: 'edit_note', permiso: 'inventario.ajustar' },
      { etiqueta: 'Traspasos', ruta: '/inventario/traspasos', icono: 'forklift', permiso: 'inventario.traspasar' },
      { etiqueta: 'Conteos físicos', ruta: '/inventario/conteos', icono: 'checklist', permiso: 'inventario.contar' },
      { etiqueta: 'Valuación', ruta: '/inventario/valuacion', icono: 'price_check', permiso: 'inventario.costos' },
      { etiqueta: 'Almacenes', ruta: '/inventario/almacenes', icono: 'warehouse', permiso: 'inventario.ver' },
    ],
  },
  {
    etiqueta: 'Compras',
    opciones: [
      { etiqueta: 'Órdenes de compra', ruta: '/compras/ordenes', icono: 'request_page', permiso: 'compras.ver' },
      { etiqueta: 'Recepciones', ruta: '/compras/recepciones', icono: 'agriculture', permiso: ['compras.recibir', 'compras.ver'] },
      { etiqueta: 'Proveedores', ruta: '/compras/proveedores', icono: 'handshake', permiso: 'compras.ver' },
    ],
  },
  {
    etiqueta: 'Logística',
    opciones: [
      { etiqueta: 'Cargas del día', ruta: '/logistica/cargas', icono: 'delivery_truck_speed', permiso: 'logistica.ver' },
      { etiqueta: 'Liquidaciones', ruta: '/logistica/liquidaciones', icono: 'account_balance_wallet', permiso: 'logistica.ver' },
      { etiqueta: 'Retornos', ruta: '/logistica/retornos', icono: 'u_turn_left', permiso: ['logistica.retornos', 'logistica.ver'] },
      { etiqueta: 'Rutas', ruta: '/logistica/rutas', icono: 'route', permiso: 'logistica.configurar' },
      { etiqueta: 'Vehículos', ruta: '/logistica/vehiculos', icono: 'local_shipping', permiso: 'logistica.configurar' },
    ],
  },
  {
    etiqueta: 'Ventas',
    opciones: [
      { etiqueta: 'Pedidos', ruta: '/ventas/pedidos', icono: 'orders', permiso: 'ventas.pedidos' },
      { etiqueta: 'Ventas', ruta: '/ventas/ventas', icono: 'storefront', permiso: 'ventas.ver' },
      { etiqueta: 'Cobros', ruta: '/ventas/cobros', icono: 'attach_money', permiso: 'ventas.cobrar' },
      { etiqueta: 'Incidencias', ruta: '/ventas/incidencias', icono: 'warning', permiso: 'ventas.supervisar' },
      { etiqueta: 'Clientes', ruta: '/ventas/clientes', icono: 'groups', permiso: 'ventas.clientes' },
      { etiqueta: 'Listas de precio', ruta: '/ventas/listas-precio', icono: 'sell', permiso: 'ventas.configurar' },
    ],
  },
  {
    etiqueta: 'Facturación',
    opciones: [
      { etiqueta: 'Facturas', ruta: '/facturacion/facturas', icono: 'receipt_long', permiso: 'facturacion.ver' },
      { etiqueta: 'Autofactura', ruta: '/facturacion/solicitudes', icono: 'qr_code_2', permiso: 'facturacion.ver' },
      { etiqueta: 'Factura global', ruta: '/facturacion/global', icono: 'library_books', permiso: 'facturacion.emitir' },
      { etiqueta: 'Emisor', ruta: '/facturacion/emisor', icono: 'domain', permiso: 'facturacion.configurar' },
    ],
  },
  {
    etiqueta: 'Catálogos',
    opciones: [
      { etiqueta: 'Productos', ruta: '/catalogos/productos', icono: 'maiz' },
      { etiqueta: 'Unidades', ruta: '/catalogos/unidades', icono: 'scale' },
      { etiqueta: 'Motivos de merma', ruta: '/catalogos/motivos-merma', icono: 'compost' },
      { etiqueta: 'Parámetros', ruta: '/catalogos/parametros', icono: 'settings_applications', permiso: 'catalogos.editar' },
      { etiqueta: 'Carga inicial', ruta: '/catalogos/importar', icono: 'upload_file', permiso: 'catalogos.editar' },
    ],
  },
  {
    etiqueta: 'Seguridad',
    opciones: [
      { etiqueta: 'Usuarios', ruta: '/seguridad/usuarios', icono: 'manage_accounts', permiso: 'seguridad.usuarios' },
      { etiqueta: 'Roles y permisos', ruta: '/seguridad/roles', icono: 'shield_person', permiso: 'seguridad.roles' },
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
