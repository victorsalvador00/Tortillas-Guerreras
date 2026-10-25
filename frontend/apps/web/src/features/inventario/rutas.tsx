import type { AnyRoute } from '@tanstack/react-router';
import { pantalla } from '@/app/rutas-base';
import { Ajustes, DetalleAjuste, NuevoAjuste } from './Ajustes';
import { Almacenes, Existencias, Kardex, Valuacion } from './Consultas';
import { Conteos, DetalleConteo } from './Conteos';
import { Trazabilidad } from './Trazabilidad';
import { DetalleTraspaso, NuevoTraspaso, Traspasos } from './Traspasos';

export function rutasInventario(): AnyRoute[] {
  return [
    pantalla('/inventario/existencias', Existencias, 'inventario.ver'),
    pantalla('/inventario/kardex', Kardex, 'inventario.ver'),
    pantalla('/inventario/almacenes', Almacenes, 'inventario.ver'),
    pantalla('/inventario/ajustes', Ajustes, 'inventario.ajustar'),
    pantalla('/inventario/ajustes/nuevo', NuevoAjuste, 'inventario.ajustar'),
    pantalla('/inventario/ajustes/$id', DetalleAjuste, 'inventario.ajustar'),
    pantalla('/inventario/traspasos', Traspasos, 'inventario.traspasar'),
    pantalla('/inventario/traspasos/nuevo', NuevoTraspaso, 'inventario.traspasar'),
    pantalla('/inventario/traspasos/$id', DetalleTraspaso, 'inventario.traspasar'),
    pantalla('/inventario/conteos', Conteos, 'inventario.contar'),
    pantalla('/inventario/conteos/$id', DetalleConteo, 'inventario.contar'),
    pantalla('/inventario/valuacion', Valuacion, 'inventario.costos'),
    pantalla('/inventario/trazabilidad', Trazabilidad, 'inventario.trazabilidad'),
  ];
}
