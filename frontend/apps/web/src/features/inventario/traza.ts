import type { AristaTrazaDto, NodoTrazaDto, TipoAristaTraza, TipoNodoTraza, TrazaDto } from '@tg/api-client';
import { formatoCantidad } from '@tg/shared';

// Mapa de trazabilidad (T-8.16). Portado del layout de panissimo y adaptado a un grafo genérico: las columnas salen del
// camino más largo desde los orígenes (así una masa que alimenta a una tortilla queda un nivel antes), el orden vertical
// por baricentro de los padres y las aristas son bandas cuyo grosor es proporcional a la cantidad.

export const ANCHO_CARRIL = 248;
export const ANCHO_TARJETA = 184;
export const ALTO_TARJETA = 88;
export const PASO = 104;
export const ARRIBA = 56;
const DELGADA = 1.3;

export const ETIQUETA_NODO: Record<TipoNodoTraza, string> = {
  Proveedor: 'Proveedor',
  Recepcion: 'Recepción',
  Lote: 'Lote',
  Produccion: 'Producción',
  Envio: 'Carga a ruta',
  Traspaso: 'Traspaso',
  Venta: 'Venta',
  Cliente: 'Cliente',
  Retorno: 'Retorno',
  Liquidacion: 'Liquidación',
  Ajuste: 'Ajuste',
  Merma: 'Merma',
  Regreso: 'Regreso',
};

export const ICONO_NODO: Record<TipoNodoTraza, string> = {
  Proveedor: 'storefront',
  Recepcion: 'receipt_long',
  Lote: 'sell',
  Produccion: 'factory',
  Envio: 'local_shipping',
  Traspaso: 'swap_horiz',
  Venta: 'point_of_sale',
  Cliente: 'store',
  Retorno: 'assignment_return',
  Liquidacion: 'fact_check',
  Ajuste: 'edit_note',
  Merma: 'delete_sweep',
  Regreso: 'keyboard_return',
};

/** Aristas que se dibujan como línea delgada (no mueven producto). */
const SIN_BANDA: ReadonlySet<TipoAristaTraza> = new Set(['Suministro']);

/** Aristas que se pintan como diferencia (rojo): lo que no siguió la cadena. */
export const ARISTA_DIFERENCIA: ReadonlySet<TipoAristaTraza> = new Set(['Merma', 'Rechazo']);

export interface CarrilTraza {
  indice: number;
  etiqueta: string;
  icono: string;
  x: number;
  cantidad: number;
}

export interface TarjetaTraza {
  id: string;
  nodo: NodoTrazaDto;
  columna: number;
  x: number;
  y: number;
}

export interface EnlaceTraza {
  id: string;
  desde: string;
  hacia: string;
  tipo: TipoAristaTraza;
  cantidad: number | null;
  unidad: string | null;
  grosor: number;
  d: string;
  etiquetaX: number;
  etiquetaY: number;
  etiqueta: string | null;
}

export interface LayoutTraza {
  carriles: CarrilTraza[];
  tarjetas: TarjetaTraza[];
  enlaces: EnlaceTraza[];
  ancho: number;
  alto: number;
}

/** Columna de cada nodo: 0 para los orígenes y 1 + la mayor columna de sus padres; un ciclo no se sigue dos veces. */
export function columnas(nodos: readonly NodoTrazaDto[], aristas: readonly AristaTrazaDto[]): Map<string, number> {
  const ids = new Set(nodos.map((n) => n.id));
  const padres = new Map<string, string[]>();
  for (const a of aristas) {
    if (!ids.has(a.desde) || !ids.has(a.hacia) || a.desde === a.hacia) continue;
    padres.set(a.hacia, [...(padres.get(a.hacia) ?? []), a.desde]);
  }
  const columna = new Map<string, number>();
  const visitando = new Set<string>();
  const de = (id: string): number => {
    const conocida = columna.get(id);
    if (conocida != null) return conocida;
    if (visitando.has(id)) return 0;
    visitando.add(id);
    const lista = padres.get(id) ?? [];
    const valor = lista.length === 0 ? 0 : 1 + Math.max(...lista.map(de));
    visitando.delete(id);
    columna.set(id, valor);
    return valor;
  };
  for (const n of nodos) de(n.id);

  // Proveedores y clientes van pegados a su documento: el proveedor justo antes de la recepción.
  for (const a of aristas)
    if (a.tipo === 'Suministro' && columna.has(a.desde) && columna.has(a.hacia))
      columna.set(a.desde, Math.max(columna.get(a.desde)!, columna.get(a.hacia)! - 1));
  return columna;
}

export function layoutTraza(traza: Pick<TrazaDto, 'nodos' | 'aristas'>): LayoutTraza {
  const columna = columnas(traza.nodos, traza.aristas);
  const usadas = [...new Set(columna.values())].sort((a, b) => a - b);
  const indice = new Map(usadas.map((c, i) => [c, i]));
  const tarjetas = new Map<string, TarjetaTraza>();
  for (const nodo of traza.nodos) {
    const i = indice.get(columna.get(nodo.id) ?? 0)!;
    tarjetas.set(nodo.id, { id: nodo.id, nodo, columna: i, x: i * ANCHO_CARRIL + (ANCHO_CARRIL - ANCHO_TARJETA) / 2, y: 0 });
  }

  const carriles: CarrilTraza[] = usadas.map((_, i) => {
    const enCarril = [...tarjetas.values()].filter((t) => t.columna === i);
    const conteo = new Map<TipoNodoTraza, number>();
    for (const t of enCarril) conteo.set(t.nodo.tipo, (conteo.get(t.nodo.tipo) ?? 0) + 1);
    const tipos = [...conteo.entries()].sort((a, b) => b[1] - a[1]).map(([tipo]) => tipo);
    return {
      indice: i,
      etiqueta: tipos.map((t) => ETIQUETA_NODO[t]).join(' · '),
      icono: ICONO_NODO[tipos[0] ?? 'Lote'],
      x: i * ANCHO_CARRIL,
      cantidad: enCarril.length,
    };
  });

  // Enlaces a nivel tarjeta.
  const enlaces: EnlaceTraza[] = traza.aristas
    .filter((a) => tarjetas.has(a.desde) && tarjetas.has(a.hacia) && a.desde !== a.hacia)
    .map((a) => ({
      id: `${a.desde}>${a.hacia}>${a.tipo}`,
      desde: a.desde,
      hacia: a.hacia,
      tipo: a.tipo,
      cantidad: a.cantidad ?? null,
      unidad: a.unidad ?? null,
      grosor: DELGADA,
      d: '',
      etiquetaX: 0,
      etiquetaY: 0,
      etiqueta: null,
    }));
  const maximo = Math.max(1, ...enlaces.filter((e) => !SIN_BANDA.has(e.tipo)).map((e) => e.cantidad ?? 0));
  for (const e of enlaces) e.grosor = SIN_BANDA.has(e.tipo) || e.cantidad == null ? DELGADA : grosorBanda(e.cantidad, maximo);

  // Orden vertical: primera columna por tipo y título; las demás por el promedio de sus padres.
  const padres = new Map<string, string[]>();
  const hijos = new Map<string, string[]>();
  for (const e of enlaces) {
    padres.set(e.hacia, [...(padres.get(e.hacia) ?? []), e.desde]);
    hijos.set(e.desde, [...(hijos.get(e.desde) ?? []), e.hacia]);
  }
  const enColumna = (i: number) => [...tarjetas.values()].filter((t) => t.columna === i);
  colocar(
    enColumna(0).sort((a, b) => a.nodo.tipo.localeCompare(b.nodo.tipo) || a.nodo.titulo.localeCompare(b.nodo.titulo, 'es')),
    null,
  );
  for (let i = 1; i < carriles.length; i++) {
    const lista = enColumna(i);
    const deseada = new Map(lista.map((t) => [t.id, promedio((padres.get(t.id) ?? []).map((p) => tarjetas.get(p)!.y))]));
    lista.sort(
      (a, b) =>
        (deseada.get(a.id) ?? Infinity) - (deseada.get(b.id) ?? Infinity) ||
        (a.nodo.fecha ?? '').localeCompare(b.nodo.fecha ?? '') ||
        a.nodo.titulo.localeCompare(b.nodo.titulo, 'es'),
    );
    colocar(lista, deseada);
  }
  // Pasada hacia atrás: la primera columna junto a sus hijos (proveedores al lado de sus recepciones).
  const primera = enColumna(0);
  const deseadaPrimera = new Map(primera.map((t) => [t.id, promedio((hijos.get(t.id) ?? []).map((h) => tarjetas.get(h)!.y))]));
  colocar(
    primera.sort((a, b) => (deseadaPrimera.get(a.id) ?? Infinity) - (deseadaPrimera.get(b.id) ?? Infinity)),
    deseadaPrimera,
  );

  // Puertos apilados y curvas.
  const salen = new Map<string, EnlaceTraza[]>();
  const llegan = new Map<string, EnlaceTraza[]>();
  for (const e of enlaces) {
    salen.set(e.desde, [...(salen.get(e.desde) ?? []), e]);
    llegan.set(e.hacia, [...(llegan.get(e.hacia) ?? []), e]);
  }
  const yOrigen = new Map<string, number>();
  const yDestino = new Map<string, number>();
  for (const t of tarjetas.values()) {
    const fuera = (salen.get(t.id) ?? []).sort((a, b) => tarjetas.get(a.hacia)!.y - tarjetas.get(b.hacia)!.y);
    for (const [id, y] of apilarPuertos(t, fuera)) yOrigen.set(id, y);
    const dentro = (llegan.get(t.id) ?? []).sort((a, b) => tarjetas.get(a.desde)!.y - tarjetas.get(b.desde)!.y);
    for (const [id, y] of apilarPuertos(t, dentro)) yDestino.set(id, y);
  }
  for (const e of enlaces) {
    const a = tarjetas.get(e.desde)!;
    const b = tarjetas.get(e.hacia)!;
    const haciaAtras = b.x <= a.x;
    const x1 = haciaAtras ? a.x + ANCHO_TARJETA / 2 : a.x + ANCHO_TARJETA;
    const x2 = haciaAtras ? b.x + ANCHO_TARJETA / 2 : b.x;
    const y1 = haciaAtras ? a.y + ALTO_TARJETA : yOrigen.get(e.id)!;
    const y2 = haciaAtras ? b.y : yDestino.get(e.id)!;
    const mx = (x1 + x2) / 2;
    e.d = haciaAtras ? `M ${x1} ${y1} C ${x1} ${y1 + 40}, ${x2} ${y2 - 40}, ${x2} ${y2}` : `M ${x1} ${y1} C ${mx} ${y1}, ${mx} ${y2}, ${x2} ${y2}`;
    const [px, py] = bezier(0.5, [x1, y1], [mx, y1], [mx, y2], [x2, y2]);
    e.etiquetaX = px;
    e.etiquetaY = py - e.grosor / 2 - 4;
    e.etiqueta = SIN_BANDA.has(e.tipo) || e.cantidad == null ? null : `${formatoCantidad(e.cantidad)} ${e.unidad ?? ''}`.trim();
  }

  const lista = [...tarjetas.values()];
  return {
    carriles,
    tarjetas: lista,
    enlaces,
    ancho: Math.max(1, carriles.length) * ANCHO_CARRIL,
    alto: Math.max(ARRIBA + ALTO_TARJETA, ...lista.map((t) => t.y + ALTO_TARJETA)) + 24,
  };

  /** Nadie más cerca que un paso; se respeta la altura deseada si cabe. */
  function colocar(fila: TarjetaTraza[], deseada: Map<string, number | null> | null): void {
    let anterior: TarjetaTraza | null = null;
    for (const t of fila) {
      const minimo = anterior ? anterior.y + PASO : ARRIBA;
      const quiere = deseada?.get(t.id);
      t.y = quiere == null ? minimo : Math.max(minimo, quiere);
      anterior = t;
    }
  }
}

/** Grosor de banda por cantidad (de 2.5 a 12 px). */
export function grosorBanda(cantidad: number, maximo: number): number {
  return Math.max(2.5, (12 * cantidad) / maximo);
}

/** Puertos apilados (Sankey) centrados en la tarjeta: 3 px entre bandas y 5 px entre líneas delgadas. */
export function apilarPuertos(tarjeta: { y: number }, enlaces: readonly { id: string; grosor: number }[]): Map<string, number> {
  const huecos = enlaces.slice(1).map((e, i) => (e.grosor <= DELGADA && enlaces[i].grosor <= DELGADA ? 5 : 3));
  const total = enlaces.reduce((s, e) => s + e.grosor, 0) + huecos.reduce((s, h) => s + h, 0);
  let cursor = tarjeta.y + ALTO_TARJETA / 2 - total / 2;
  const resultado = new Map<string, number>();
  enlaces.forEach((e, i) => {
    resultado.set(e.id, cursor + e.grosor / 2);
    cursor += e.grosor + (huecos[i] ?? 0);
  });
  return resultado;
}

/** Cadena de un nodo: todo lo que lo alimentó y todo lo que salió de él (para resaltar y atenuar lo demás). */
export function cadena(aristas: readonly Pick<AristaTrazaDto, 'desde' | 'hacia'>[], id: string): Set<string> {
  const resultado = new Set<string>([id]);
  const recorrer = (siguiente: (a: Pick<AristaTrazaDto, 'desde' | 'hacia'>) => [string, string]) => {
    const pendientes = [id];
    const vistos = new Set<string>([id]);
    while (pendientes.length > 0) {
      const actual = pendientes.pop()!;
      for (const a of aristas) {
        const [de, a2] = siguiente(a);
        if (de === actual && !vistos.has(a2)) {
          vistos.add(a2);
          resultado.add(a2);
          pendientes.push(a2);
        }
      }
    }
  };
  recorrer((a) => [a.desde, a.hacia]);
  recorrer((a) => [a.hacia, a.desde]);
  return resultado;
}

export interface ResumenTraza {
  proveedores: NodoTrazaDto[];
  recepciones: NodoTrazaDto[];
  lotes: { productoId: string; sku: string; producto: string; lote: string; caducidad: string | null; existencia: number; unidad: string; almacenes: string[] }[];
  ordenes: NodoTrazaDto[];
  cargas: NodoTrazaDto[];
  ventas: NodoTrazaDto[];
  clientes: NodoTrazaDto[];
  merma: { cantidad: number; unidad: string | null };
  rechazo: { cantidad: number; unidad: string | null };
}

/** Totales de la pestaña Resumen, calculados del mismo grafo. */
export function resumenTraza(traza: Pick<TrazaDto, 'nodos' | 'aristas'>): ResumenTraza {
  const de = (tipo: TipoNodoTraza) => traza.nodos.filter((n) => n.tipo === tipo);
  const lotes = new Map<string, ResumenTraza['lotes'][number]>();
  for (const n of de('Lote')) {
    const clave = `${n.productoId}|${n.lote}`;
    const actual = lotes.get(clave) ?? {
      productoId: n.productoId ?? '',
      sku: n.sku ?? '',
      producto: n.producto ?? '',
      lote: n.lote ?? n.titulo,
      caducidad: n.caducidad ?? null,
      existencia: 0,
      unidad: n.unidad ?? '',
      almacenes: [],
    };
    actual.existencia += n.existencia ?? 0;
    if (n.almacen) actual.almacenes.push(n.almacen);
    lotes.set(clave, actual);
  }
  const sumar = (tipo: TipoAristaTraza) => {
    const lista = traza.aristas.filter((a) => a.tipo === tipo);
    const unidades = [...new Set(lista.map((a) => a.unidad ?? ''))];
    return { cantidad: lista.reduce((s, a) => s + (a.cantidad ?? 0), 0), unidad: unidades.length === 1 ? unidades[0] || null : null };
  };
  return {
    proveedores: de('Proveedor'),
    recepciones: de('Recepcion'),
    lotes: [...lotes.values()].sort((a, b) => a.producto.localeCompare(b.producto, 'es') || a.lote.localeCompare(b.lote)),
    ordenes: de('Produccion'),
    cargas: de('Envio'),
    ventas: de('Venta'),
    clientes: de('Cliente'),
    merma: sumar('Merma'),
    rechazo: sumar('Rechazo'),
  };
}

/** Columnas del CSV de la bitácora. */
export const COLUMNAS_CSV_TRAZA = [
  { titulo: 'Fecha', valor: (e: TrazaDto['eventos'][number]) => e.fechaHora },
  { titulo: 'Fecha operativa', valor: (e: TrazaDto['eventos'][number]) => e.fechaOperativa },
  { titulo: 'Movimiento', valor: (e: TrazaDto['eventos'][number]) => e.tipo },
  { titulo: 'Documento', valor: (e: TrazaDto['eventos'][number]) => e.documentoTipo },
  { titulo: 'Folio', valor: (e: TrazaDto['eventos'][number]) => e.folio },
  { titulo: 'Almacén', valor: (e: TrazaDto['eventos'][number]) => e.almacen },
  { titulo: 'SKU', valor: (e: TrazaDto['eventos'][number]) => e.sku },
  { titulo: 'Producto', valor: (e: TrazaDto['eventos'][number]) => e.producto },
  { titulo: 'Lote', valor: (e: TrazaDto['eventos'][number]) => e.lote },
  { titulo: 'Cantidad', valor: (e: TrazaDto['eventos'][number]) => e.cantidad },
  { titulo: 'Unidad', valor: (e: TrazaDto['eventos'][number]) => e.unidad },
];

/** La opción del buscador viaja como texto: `tipo|id|productoId|lote`. */
export function codificarEntrada(e: { tipo: string; id?: string | null; productoId?: string | null; lote?: string | null }): string {
  return [e.tipo, e.id ?? '', e.productoId ?? '', e.lote ?? ''].join('|');
}

export function decodificarEntrada(valor: string): { tipo: string; id?: string; productoId?: string; lote?: string } {
  const [tipo, id, productoId, ...lote] = valor.split('|');
  return { tipo, id: id || undefined, productoId: productoId || undefined, lote: lote.join('|') || undefined };
}

function promedio(valores: number[]): number | null {
  return valores.length === 0 ? null : valores.reduce((a, b) => a + b, 0) / valores.length;
}

function bezier(t: number, p0: [number, number], p1: [number, number], p2: [number, number], p3: [number, number]): [number, number] {
  const u = 1 - t;
  const f = (i: 0 | 1) => u * u * u * p0[i] + 3 * u * u * t * p1[i] + 3 * u * t * t * p2[i] + t * t * t * p3[i];
  return [f(0), f(1)];
}
