import type { AristaTrazaDto, NodoTrazaDto, TipoNodoTraza } from '@tg/api-client';
import { describe, expect, it } from 'vitest';
import {
  ALTO_TARJETA,
  ANCHO_CARRIL,
  PASO,
  apilarPuertos,
  cadena,
  codificarEntrada,
  columnas,
  decodificarEntrada,
  grosorBanda,
  layoutTraza,
  resumenTraza,
} from './traza';

const nodo = (id: string, tipo: TipoNodoTraza, extra: Partial<NodoTrazaDto> = {}): NodoTrazaDto => ({
  id,
  tipo,
  titulo: id,
  datos: [],
  consumoSinLote: [],
  ...extra,
});

const arista = (desde: string, hacia: string, tipo: AristaTrazaDto['tipo'], cantidad: number | null = null): AristaTrazaDto => ({
  desde,
  hacia,
  tipo,
  cantidad,
  unidad: cantidad == null ? null : 'KG',
});

// Proveedor → 2 recepciones → 2 lotes de maíz → OP masa → lote masa → OP tortilla → lote tortilla → venta → cliente.
const nodos = [
  nodo('prov', 'Proveedor'),
  nodo('recA', 'Recepcion'),
  nodo('recB', 'Recepcion'),
  nodo('maizA', 'Lote', { productoId: 'maiz', lote: 'A', existencia: 0, unidad: 'KG', almacen: 'MP · Materia prima' }),
  nodo('maizB', 'Lote', { productoId: 'maiz', lote: 'B', existencia: 22, unidad: 'KG', almacen: 'MP · Materia prima' }),
  nodo('opMasa', 'Produccion'),
  nodo('masa', 'Lote', { productoId: 'masa', lote: 'M1', existencia: 50, unidad: 'KG' }),
  nodo('opTort', 'Produccion'),
  nodo('tortPT', 'Lote', { productoId: 'tort', lote: 'T1', existencia: 20, unidad: 'KG', almacen: 'PT · Terminado' }),
  nodo('tortRuta', 'Lote', { productoId: 'tort', lote: 'T1', existencia: 20, unidad: 'KG', almacen: 'R01 · Ruta' }),
  nodo('venta', 'Venta'),
  nodo('cli', 'Cliente'),
  nodo('merma', 'Merma'),
];
const aristas = [
  arista('prov', 'recA', 'Suministro'),
  arista('prov', 'recB', 'Suministro'),
  arista('recA', 'maizA', 'Entrada', 40),
  arista('recB', 'maizB', 'Entrada', 40),
  arista('maizA', 'opMasa', 'Consumo', 40),
  arista('maizB', 'opMasa', 'Consumo', 18),
  arista('opMasa', 'masa', 'Produccion', 100),
  arista('masa', 'opTort', 'Consumo', 50),
  arista('opTort', 'tortPT', 'Produccion', 50),
  arista('tortPT', 'tortRuta', 'Salida', 30),
  arista('tortRuta', 'venta', 'Venta', 10),
  arista('venta', 'cli', 'Entrega', 10),
  arista('opTort', 'merma', 'Merma', 2),
];
const traza = { nodos, aristas };

describe('layout del mapa de trazabilidad', () => {
  it('ordena las columnas por el camino más largo: la masa queda un nivel antes que la tortilla', () => {
    const c = columnas(nodos, aristas);
    expect(c.get('prov')).toBe(0);
    expect(c.get('recA')).toBe(1);
    expect(c.get('maizA')).toBe(2);
    expect(c.get('opMasa')).toBe(3);
    expect(c.get('masa')).toBe(4);
    expect(c.get('opTort')).toBe(5);
    expect(c.get('cli')).toBe(9);
  });

  it('no se cicla cuando un lote va y regresa entre almacenes', () => {
    const c = columnas([nodo('a', 'Lote'), nodo('t', 'Traspaso'), nodo('b', 'Lote')], [
      arista('a', 't', 'Salida', 1),
      arista('t', 'b', 'Llegada', 1),
      arista('b', 't', 'Salida', 1),
    ]);
    expect(c.size).toBe(3);
  });

  it('coloca tarjetas por carril sin encimarse y con bandas proporcionales', () => {
    const layout = layoutTraza(traza);
    expect(layout.carriles).toHaveLength(10);
    expect(layout.carriles[0].etiqueta).toBe('Proveedor');
    const recA = layout.tarjetas.find((t) => t.id === 'recA')!;
    const recB = layout.tarjetas.find((t) => t.id === 'recB')!;
    expect(recA.x).toBe(ANCHO_CARRIL + (ANCHO_CARRIL - 184) / 2);
    expect(Math.abs(recA.y - recB.y)).toBeGreaterThanOrEqual(PASO);
    const consumoA = layout.enlaces.find((e) => e.desde === 'maizA')!;
    const consumoB = layout.enlaces.find((e) => e.desde === 'maizB')!;
    expect(consumoA.grosor).toBeGreaterThan(consumoB.grosor);
    expect(consumoA.etiqueta).toBe('40 KG');
    expect(layout.enlaces.find((e) => e.tipo === 'Suministro')!.etiqueta).toBeNull();
    expect(layout.alto).toBeGreaterThanOrEqual(Math.max(...layout.tarjetas.map((t) => t.y)) + ALTO_TARJETA);
  });

  it('pone al proveedor a la altura de sus recepciones', () => {
    const layout = layoutTraza(traza);
    const y = (id: string) => layout.tarjetas.find((t) => t.id === id)!.y;
    expect(y('prov')).toBe((y('recA') + y('recB')) / 2);
  });

  it('banda mínima de 2.5 px y puertos apilados centrados en la tarjeta', () => {
    expect(grosorBanda(1, 1000)).toBe(2.5);
    expect(grosorBanda(1000, 1000)).toBe(12);
    const puertos = apilarPuertos({ y: 0 }, [
      { id: 'a', grosor: 10 },
      { id: 'b', grosor: 10 },
    ]);
    expect(puertos.get('a')).toBe(ALTO_TARJETA / 2 - 6.5);
    expect(puertos.get('b')).toBe(ALTO_TARJETA / 2 + 6.5);
  });
});

describe('selección de la cadena', () => {
  it('resalta lo que alimentó al nodo y lo que salió de él, no las ramas hermanas', () => {
    const c = cadena(aristas, 'maizA');
    expect(c.has('recA')).toBe(true);
    expect(c.has('prov')).toBe(true);
    expect(c.has('cli')).toBe(true);
    expect(c.has('maizB')).toBe(false);
    expect(c.has('recB')).toBe(false);
  });
});

describe('resumen y entrada del buscador', () => {
  it('cuenta lotes únicos y suma existencia y merma', () => {
    const r = resumenTraza(traza);
    expect(r.proveedores).toHaveLength(1);
    expect(r.recepciones).toHaveLength(2);
    expect(r.lotes).toHaveLength(4);
    expect(r.lotes.find((l) => l.lote === 'T1')!.existencia).toBe(40);
    expect(r.lotes.find((l) => l.lote === 'T1')!.almacenes).toEqual(['PT · Terminado', 'R01 · Ruta']);
    expect(r.merma).toEqual({ cantidad: 2, unidad: 'KG' });
  });

  it('codifica y decodifica la opción, incluido un lote con barra vertical', () => {
    const valor = codificarEntrada({ tipo: 'Lote', productoId: 'p1', lote: 'L|1' });
    expect(decodificarEntrada(valor)).toEqual({ tipo: 'Lote', id: undefined, productoId: 'p1', lote: 'L|1' });
    expect(decodificarEntrada(codificarEntrada({ tipo: 'Venta', id: 'v1' }))).toEqual({
      tipo: 'Venta',
      id: 'v1',
      productoId: undefined,
      lote: undefined,
    });
  });
});
