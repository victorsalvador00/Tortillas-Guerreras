import { describe, expect, it } from 'vitest';
import { aCentavos, calcularPartida, calcularTotales, redondearAwayFromZero } from './dinero';
import { fechaOperativa, formatoFecha, sumarDias } from './formatos';
import { rfc } from './esquemas';

describe('dinero (RN-VEN-02 / RN-APP-06)', () => {
  it('redondea la mitad alejándose de cero como .NET', () => {
    expect(redondearAwayFromZero(0.5)).toBe(1);
    expect(redondearAwayFromZero(-0.5)).toBe(-1);
    expect(redondearAwayFromZero(234.5)).toBe(235);
    expect(aCentavos(2.675)).toBe(268);
  });

  it('calcula importe, IEPS sobre importe e IVA sobre importe + IEPS', () => {
    // Totopo: 3 bolsas a $18.50, IEPS 8 %, IVA 16 %.
    const p = calcularPartida({ cantidad: 3, precioUnitario: 18.5, tasaIva: 0.16, tasaIeps: 0.08 });
    expect(p.importe).toBe(5550);
    expect(p.ieps).toBe(444);
    expect(p.iva).toBe(959); // (5550 + 444) × 0.16 = 959.04
    expect(p.total).toBe(6953);
  });

  it('redondea por partida y luego suma', () => {
    const t = calcularTotales([
      { cantidad: 1.333, precioUnitario: 22, tasaIva: 0 },
      { cantidad: 2.5, precioUnitario: 21.9999, tasaIva: 0 },
    ]);
    expect(t.subtotal).toBe(2933 + 5500);
    expect(t.total).toBe(t.subtotal);
  });

  it('5 kg de tortilla a $22.00 = $110.00 (ejemplo del protocolo de sync)', () => {
    expect(calcularPartida({ cantidad: 5, precioUnitario: 22, tasaIva: 0 }).total).toBe(11000);
  });
});

describe('formatos', () => {
  it('fecha operativa en hora de México: 23:59 vs 00:01', () => {
    expect(fechaOperativa(new Date('2026-11-04T05:59:00Z'))).toBe('2026-11-03');
    expect(fechaOperativa(new Date('2026-11-04T06:01:00Z'))).toBe('2026-11-04');
  });

  it('formatea fechas de negocio sin cambiar de zona', () => {
    expect(formatoFecha('2026-11-03')).toBe('03/11/2026');
    expect(sumarDias('2026-11-30', 2)).toBe('2026-12-02');
  });

  it('valida RFC de 12 y 13 caracteres (RN-FAC-03)', () => {
    expect(rfc.safeParse('XAXX010101000').success).toBe(true);
    expect(rfc.safeParse('ABC010101AB1').success).toBe(true);
    expect(rfc.safeParse('ABC').success).toBe(false);
  });
});
