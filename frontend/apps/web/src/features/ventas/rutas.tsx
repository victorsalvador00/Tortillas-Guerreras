import type { AnyRoute } from '@tanstack/react-router';
import { pantalla } from '@/app/rutas-base';
import { ClienteFormulario, Clientes } from './Clientes';
import { Cobros, Incidencias } from './CobrosIncidencias';
import { GruposClientes } from './GruposPrecios';
import { ListaPrecioDetalle, ListasPrecio } from './ListasPrecio';
import { PedidoDetalle, PedidoFormulario, Pedidos } from './Pedidos';
import { NuevaVenta, VentaDetalle, Ventas } from './Ventas';

/** Pantallas de Ventas (07-web); permisos según Permisos.cs. */
export function rutasVentas(): AnyRoute[] {
  return [
    pantalla('/ventas/clientes', Clientes, ['ventas.clientes', 'ventas.supervisar']),
    pantalla('/ventas/clientes/nuevo', ClienteFormulario, 'ventas.clientes'),
    pantalla('/ventas/clientes/$id', ClienteFormulario, ['ventas.clientes', 'ventas.supervisar']),
    pantalla('/ventas/listas-precio', ListasPrecio, 'ventas.configurar'),
    pantalla('/ventas/grupos-clientes', GruposClientes, 'ventas.configurar'),
    pantalla('/ventas/listas-precio/$id', ListaPrecioDetalle, 'ventas.configurar'),
    pantalla('/ventas/pedidos', Pedidos, 'ventas.pedidos'),
    pantalla('/ventas/pedidos/nuevo', PedidoFormulario, 'ventas.pedidos'),
    pantalla('/ventas/pedidos/$id', PedidoDetalle, 'ventas.pedidos'),
    pantalla('/ventas/pedidos/$id/editar', PedidoFormulario, 'ventas.pedidos'),
    pantalla('/ventas/ventas', Ventas, 'ventas.ver'),
    pantalla('/ventas/ventas/nueva', NuevaVenta, 'ventas.registrar'),
    pantalla('/ventas/ventas/$id', VentaDetalle, 'ventas.ver'),
    pantalla('/ventas/cobros', Cobros, 'ventas.cobrar'),
    pantalla('/ventas/incidencias', Incidencias, 'ventas.supervisar'),
  ];
}
