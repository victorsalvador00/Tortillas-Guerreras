import { describe, expect, it } from 'vitest';
import { agregarPorComponente } from './Variacion';
import { avanceOrden, consumoTeorico, costoEstimado, fueraDeUmbral, teoricoAjustado, variacion } from './calculos';

describe('producción: cálculos de la orden', () => {
  it('Tortilla v1 (100 KG masa → 88 KG): planear 176 KG pide 200 KG de masa (RN-PRO-02)', () => {
    expect(consumoTeorico(100, 176, 88)).toBe(200);
  });

  it('recalcula el teórico con lo producido (RN-PRO-03)', () => {
    expect(teoricoAjustado(200, 176, 170)).toBe(193.1818);
    expect(teoricoAjustado(200, 176, null)).toBe(200);
  });

  it('marca la variación fuera del umbral (RN-PRO-08)', () => {
    const v = variacion(210, 200);
    expect(v).toBeCloseTo(0.05);
    expect(fueraDeUmbral(v, 0.05)).toBe(false);
    expect(fueraDeUmbral(variacion(215, 200), 0.05)).toBe(true);
    expect(variacion(1, 0)).toBeNull();
  });

  it('costo unitario = suma de consumos por costo promedio entre producido (RN-PRO-05)', () => {
    const c = costoEstimado(
      [
        { real: 200, costoUnitario: 9.5 },
        { real: 0.6, costoUnitario: 120 },
      ],
      170,
    );
    expect(c.total).toBe(1972);
    expect(c.unitario).toBeCloseTo(11.6, 4);
  });

  it('calcula el avance para el plan del día', () => {
    expect(avanceOrden('Terminada', 176, 170)).toBe(97);
    expect(avanceOrden('Planeada', 100, null)).toBe(0);
  });

  it('agrega la variación por componente del periodo', () => {
    const comp = (productoId: string, teoricoAjustado: number, real: number) => ({
      productoId, sku: productoId, producto: productoId, unidad: 'KG', teoricoPlaneado: teoricoAjustado, teoricoAjustado, real,
      diferencia: real - teoricoAjustado, variacionPct: null, fueraDeUmbral: false,
    });
    const orden = (componentes: ReturnType<typeof comp>[]) => ({
      ordenId: 'o', folio: 'OP', fechaOperativa: '2026-11-03', turno: 'Matutino' as const, productoId: 'p', sku: 'p', producto: 'p',
      cantidadPlaneada: 1, cantidadProducida: 1, mermaTotal: 0, fueraDeUmbral: false, componentes,
    });
    const r = agregarPorComponente([orden([comp('masa', 100, 110)]), orden([comp('masa', 100, 100), comp('bolsa', 50, 50)])]);
    expect(r[0].componente).toBe('masa');
    expect(r[0].variacion).toBeCloseTo(0.05);
    expect(r[1].variacion).toBe(0);
  });
});
