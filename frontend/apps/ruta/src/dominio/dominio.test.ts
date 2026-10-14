import { describe, expect, it } from 'vitest';
import { consecutivoInicial, formatearFolio, uuidv7 } from './identificadores';
import { calcularVenta, clientesDelDia, diaSemanaIso, inventarioCamioneta, validarExistencia } from './ventas';

describe('folios e identificadores', () => {
  it('RN-VEN-09: folio <ruta>-<dispositivo>-<consecutivo 6 dígitos>', () => {
    expect(formatearFolio('R01', 'D03', 123)).toBe('R01-D03-000123');
    expect(() => formatearFolio('R01', 'D03', 0)).toThrow();
  });

  it('RN-SYN-08: el consecutivo nunca retrocede al reinstalar', () => {
    expect(consecutivoInicial(0, 45)).toBe(45);
    expect(consecutivoInicial(50, 45)).toBe(50);
    expect(consecutivoInicial(null, null)).toBe(0);
  });

  it('UUID v7 ordenable por tiempo', () => {
    const azar = (n: number) => new Uint8Array(n).fill(0xab);
    const a = uuidv7(azar, 1_700_000_000_000);
    const b = uuidv7(azar, 1_700_000_000_001);
    expect(a).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(a < b).toBe(true);
  });
});

describe('venta en ruta', () => {
  it('RN-APP-06: total en centavos igual al cálculo del servidor (RN-VEN-02)', () => {
    const v = calcularVenta([
      { productoId: 'tortilla', unidadId: 'paq', cantidad: 5, factorABase: 1, precioUnitario: 22, tasaIva: 0, tasaIeps: null },
      { productoId: 'totopo', unidadId: 'paq', cantidad: 3, factorABase: 1, precioUnitario: 18.5, tasaIva: 0.16, tasaIeps: 0.08 },
    ]);
    expect(v.total).toBe(11000 + 6953);
    expect(v.subtotal).toBe(11000 + 5550);
    expect(v.ieps).toBe(444);
    expect(v.iva).toBe(959);
  });

  it('convierte a unidad base con el factor', () => {
    const v = calcularVenta([{ productoId: 'p', unidadId: 'caj', cantidad: 2, factorABase: 12, precioUnitario: 10, tasaIva: 0, tasaIeps: null }]);
    expect(v.partidas[0].cantidadBase).toBe(24);
  });

  it('inventario de camioneta = carga − ventas + retornos vendibles', () => {
    const inv = inventarioCamioneta(
      [{ productoId: 't', cantidadBase: 300 }],
      [{ productoId: 't', cantidadBase: 260 }],
      [
        { productoId: 't', cantidadBase: 5, condicion: 'NoVendible', tipo: 'DevolucionCliente' },
        { productoId: 't', cantidadBase: 2, condicion: 'Vendible', tipo: 'DevolucionCliente' },
        { productoId: 't', cantidadBase: 9, condicion: 'Vendible', tipo: 'RechazoEntrega' },
      ],
    );
    expect(inv.get('t')).toEqual({ productoId: 't', cargado: 300, vendido: 260, devuelto: 2, existencia: 42 });
  });

  it('RN-APP-02: no permite vender más de la existencia si la ruta no permite negativos', () => {
    const r = validarExistencia([{ productoId: 't', cantidadBase: 12 }], new Map([['t', 10]]), false);
    expect(r.permitido).toBe(false);
    expect(r.faltantes).toEqual([{ productoId: 't', disponible: 10, requerido: 12 }]);
  });

  it('RN-APP-02: con existencia negativa permitida advierte y deja continuar [SUP-10]', () => {
    const r = validarExistencia([{ productoId: 't', cantidadBase: 12 }], new Map([['t', 10]]), true);
    expect(r.permitido).toBe(true);
    expect(r.faltantes).toHaveLength(1);
  });

  it('RN-APP-02: suma partidas del mismo producto antes de comparar', () => {
    const r = validarExistencia(
      [
        { productoId: 't', cantidadBase: 6 },
        { productoId: 't', cantidadBase: 5 },
      ],
      new Map([['t', 10]]),
      false,
    );
    expect(r.permitido).toBe(false);
  });

  it('clientes de hoy según días de visita, en orden y el genérico al final', () => {
    expect(diaSemanaIso('2026-11-03')).toBe(2); // martes
    const lista = clientesDelDia(
      [
        { id: 'gen', diasVisita: [], ordenVisita: 0, esGenerico: true },
        { id: 'b', diasVisita: [2, 4], ordenVisita: 2, esGenerico: false },
        { id: 'a', diasVisita: [2], ordenVisita: 1, esGenerico: false },
        { id: 'c', diasVisita: [1], ordenVisita: 0, esGenerico: false },
      ],
      '2026-11-03',
    );
    expect(lista.map((c) => c.id)).toEqual(['a', 'b', 'gen']);
  });
});
