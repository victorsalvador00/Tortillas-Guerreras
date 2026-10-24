import type { AnyRoute } from '@tanstack/react-router';
import { pantalla } from '@/app/rutas-base';
import { Estimado } from './Estimado';
import { Lineas } from './Lineas';
import { OrdenProduccion } from './OrdenProduccion';
import { OrdenesProduccion, PlanDia } from './Ordenes';
import { PlanSugerido } from './PlanSugerido';
import { RecetaDetalle, RecetaNueva, Recetas } from './Recetas';
import { Variacion } from './Variacion';

export function rutasProduccion(): AnyRoute[] {
  return [
    pantalla('/produccion/plan', PlanDia, 'produccion.ver'),
    pantalla('/produccion/estimado', Estimado, 'produccion.ver'),
    pantalla('/produccion/plan-sugerido', PlanSugerido, 'produccion.ver'),
    pantalla('/produccion/ordenes', OrdenesProduccion, 'produccion.ver'),
    pantalla('/produccion/ordenes/$id', OrdenProduccion, 'produccion.ver'),
    pantalla('/produccion/recetas', Recetas, 'produccion.ver'),
    pantalla('/produccion/recetas/nueva', RecetaNueva, 'produccion.recetas'),
    pantalla('/produccion/recetas/$id', RecetaDetalle, 'produccion.ver'),
    pantalla('/produccion/lineas', Lineas, 'produccion.ver'),
    pantalla('/produccion/variacion', Variacion, 'produccion.ver'),
  ];
}
