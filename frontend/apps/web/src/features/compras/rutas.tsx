import type { AnyRoute } from '@tanstack/react-router';
import { pantalla } from '@/app/rutas-base';
import { OrdenCompraDetalle } from './OrdenCompraDetalle';
import { OrdenCompraFormulario } from './OrdenCompraFormulario';
import { OrdenesCompra } from './OrdenesCompra';
import { Proveedores } from './Proveedores';
import { RecepcionDetalle, Recepciones } from './Recepciones';
import { RecepcionNueva } from './RecepcionNueva';

const VER_RECEPCIONES = ['compras.recibir', 'compras.ver'];

export function rutasCompras(): AnyRoute[] {
  return [
    pantalla('/compras/proveedores', Proveedores, 'compras.ver'),
    pantalla('/compras/ordenes', OrdenesCompra, 'compras.ver'),
    pantalla('/compras/ordenes/nueva', OrdenCompraFormulario, 'compras.editar'),
    pantalla('/compras/ordenes/$id', OrdenCompraDetalle, 'compras.ver'),
    pantalla('/compras/ordenes/$id/editar', OrdenCompraFormulario, 'compras.editar'),
    pantalla('/compras/recepciones', Recepciones, VER_RECEPCIONES),
    pantalla('/compras/recepciones/nueva', RecepcionNueva, 'compras.recibir'),
    pantalla('/compras/recepciones/$id', RecepcionDetalle, VER_RECEPCIONES),
  ];
}
