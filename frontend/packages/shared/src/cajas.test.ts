import { describe, expect, it } from 'vitest';
import { desglosarCajas, formatoCajas } from './cajas';

// Paquete de 0.8 kg y caja de 24 paquetes [SUP-26].
const t800 = [
  { unidadClave: 'PAQ', factorABase: 0.8, predeterminadaVenta: true },
  { unidadClave: 'CAJ', factorABase: 19.2, predeterminadaVenta: false, predeterminadaTraslado: true },
];

describe('cajas y paquetes (RN-CAT-09)', () => {
  it('desglosa 50 paquetes en 2 cajas + 2 paquetes', () => {
    const d = desglosarCajas(50 * 0.8, t800, 'KG');
    expect(d).toEqual({ cajas: 2, unidadTraslado: 'CAJ', paquetes: 2, unidadVenta: 'PAQ' });
    expect(formatoCajas(d!)).toBe('2 CAJ + 2 PAQ');
  });

  it('muestra solo cajas o solo paquetes cuando el otro es cero', () => {
    expect(formatoCajas(desglosarCajas(48 * 0.8, t800, 'KG')!)).toBe('2 CAJ');
    expect(formatoCajas(desglosarCajas(23 * 0.8, t800, 'KG')!)).toBe('23 PAQ');
  });

  it('sin unidad de traslado no hay desglose; sin unidad de venta usa la base', () => {
    expect(desglosarCajas(10, [t800[0]!], 'KG')).toBeNull();
    const tostada = [{ unidadClave: 'CAJ', factorABase: 15, predeterminadaVenta: false, predeterminadaTraslado: true }];
    expect(formatoCajas(desglosarCajas(32, tostada, 'PAQ')!)).toBe('2 CAJ + 2 PAQ');
  });

  it('conserva el signo en faltantes', () => {
    expect(desglosarCajas(-26 * 0.8, t800, 'KG')).toMatchObject({ cajas: -1, paquetes: -2 });
  });
});
