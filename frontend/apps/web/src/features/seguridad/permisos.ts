import type { PermisoDto } from '@tg/api-client';

const ETIQUETAS_MODULO: Record<string, string> = {
  tablero: 'Tablero',
  seguridad: 'Seguridad',
  catalogos: 'Catálogos',
  compras: 'Compras',
  produccion: 'Producción',
  inventario: 'Inventario',
  logistica: 'Logística',
  ventas: 'Ventas',
  facturacion: 'Facturación',
  sync: 'App de ruta',
};

const ORDEN = Object.keys(ETIQUETAS_MODULO);

export interface GrupoPermisos {
  modulo: string;
  etiqueta: string;
  permisos: PermisoDto[];
}

/** Agrupa los permisos por módulo (prefijo antes del punto) en el orden del menú. */
export function agruparPermisos(permisos: PermisoDto[]): GrupoPermisos[] {
  const grupos = new Map<string, PermisoDto[]>();
  for (const p of permisos) {
    const modulo = p.clave.split('.')[0];
    grupos.set(modulo, [...(grupos.get(modulo) ?? []), p]);
  }
  return [...grupos.entries()]
    .map(([modulo, lista]) => ({
      modulo,
      etiqueta: ETIQUETAS_MODULO[modulo] ?? modulo,
      permisos: [...lista].sort((a, b) => a.clave.localeCompare(b.clave)),
    }))
    .sort((a, b) => (ORDEN.indexOf(a.modulo) + 1 || 99) - (ORDEN.indexOf(b.modulo) + 1 || 99));
}

/** Marca o desmarca todos los permisos de un grupo. */
export function alternarGrupo(seleccion: string[], grupo: GrupoPermisos, marcar: boolean): string[] {
  const claves = grupo.permisos.map((p) => p.clave);
  return marcar ? [...new Set([...seleccion, ...claves])] : seleccion.filter((c) => !claves.includes(c));
}
