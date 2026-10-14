import { describe, expect, it } from 'vitest';
import { aPartidasAjuste, aPartidasTraspaso, nuevaPartida, validarPartidas, type PartidaCaptura } from './partidas';

const tortilla = { id: 'p1', sku: 'PT-TORT-KG', nombre: 'Tortilla', manejaLote: true, unidadBaseClave: 'KG' };
const maiz = { id: 'p2', sku: 'MP-MAIZ-BCO', nombre: 'Maíz', manejaLote: false, unidadBaseClave: 'KG' };

function partida(cambios: Partial<PartidaCaptura>): PartidaCaptura {
  return { ...nuevaPartida(), ...cambios };
}

describe('partidas de inventario', () => {
  it('exige al menos una partida', () => {
    expect(validarPartidas([]).general).toBeDefined();
  });

  it('valida producto, cantidad positiva y decimales', () => {
    const p = partida({ cantidad: '0' });
    const errores = validarPartidas([p]);
    expect(errores[`${p.clave}.producto`]).toBeDefined();
    expect(errores[`${p.clave}.cantidad`]).toBeDefined();
    const decimales = partida({ producto: maiz, cantidad: '1.23456' });
    expect(validarPartidas([decimales])[`${decimales.clave}.cantidad`]).toMatch(/4 decimales/);
  });

  it('en ajustes exige lote y costo en entradas de productos con lote (RN-INV-03, RN-INV-05)', () => {
    const p = partida({ producto: tortilla, cantidad: '5', sentido: 'Entrada' });
    const errores = validarPartidas([p], { exigirLoteEnEntradas: true, exigirCostoEnEntradas: true });
    expect(errores[`${p.clave}.lote`]).toBeDefined();
    expect(errores[`${p.clave}.costo`]).toBeDefined();
    const salida = partida({ producto: tortilla, cantidad: '5', sentido: 'Salida' });
    expect(validarPartidas([salida], { exigirLoteEnEntradas: true, exigirCostoEnEntradas: true })).toEqual({});
  });

  it('convierte a solicitudes con signo para ajustes y positivas para traspasos', () => {
    const entrada = partida({ producto: maiz, cantidad: '10', sentido: 'Entrada', costo: '6.5' });
    const salida = partida({ producto: tortilla, cantidad: '3', sentido: 'Salida', lote: '' });
    expect(aPartidasAjuste([entrada, salida])).toEqual([
      { productoId: 'p2', lote: null, cantidad: 10, costoUnitario: 6.5, fechaCaducidad: null },
      { productoId: 'p1', lote: null, cantidad: -3, costoUnitario: null, fechaCaducidad: null },
    ]);
    expect(aPartidasTraspaso([salida])).toEqual([{ productoId: 'p1', lote: null, cantidad: 3 }]);
  });
});
