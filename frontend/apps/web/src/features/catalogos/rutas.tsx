import type { AnyRoute } from '@tanstack/react-router';
import { pantalla } from '@/app/rutas-base';
import { CargaInicial } from './CargaInicial';
import { MotivosMerma, Parametros, Unidades } from './CatalogosMenores';
import { ProductoFormulario } from './ProductoFormulario';
import { Productos } from './Productos';

export function rutasCatalogos(): AnyRoute[] {
  return [
    pantalla('/catalogos/productos', Productos),
    pantalla('/catalogos/productos/nuevo', ProductoFormulario, 'catalogos.editar'),
    pantalla('/catalogos/productos/$id', ProductoFormulario),
    pantalla('/catalogos/unidades', Unidades),
    pantalla('/catalogos/motivos-merma', MotivosMerma),
    pantalla('/catalogos/parametros', Parametros, 'catalogos.editar'),
    pantalla('/catalogos/importar', CargaInicial, ['catalogos.editar', 'compras.editar', 'ventas.clientes', 'produccion.recetas', 'inventario.ajustar']),
  ];
}
