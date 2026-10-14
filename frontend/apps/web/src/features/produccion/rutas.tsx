import type { AnyRoute } from '@tanstack/react-router';
import { pantalla } from '@/app/rutas-base';
import { Lineas } from './Lineas';
import { OrdenProduccion } from './OrdenProduccion';
import { OrdenesProduccion, PlanDia } from './Ordenes';
import { RecetaDetalle, RecetaNueva, Recetas } from './Recetas';
import { Variacion } from './Variacion';

export function rutasProduccion(): AnyRoute[] {
  return [
    pantalla('/produccion/plan', PlanDia, 'produccion.ver'),
    pantalla('/produccion/ordenes', OrdenesProduccion, 'produccion.ver'),
    pantalla('/produccion/ordenes/$id', OrdenProduccion, 'produccion.ver'),
    pantalla('/produccion/recetas', Recetas, 'produccion.ver'),
    pantalla('/produccion/recetas/nueva', RecetaNueva, 'produccion.recetas'),
    pantalla('/produccion/recetas/$id', RecetaDetalle, 'produccion.ver'),
    pantalla('/produccion/lineas', Lineas, 'produccion.ver'),
    pantalla('/produccion/variacion', Variacion, 'produccion.ver'),
  ];
}
