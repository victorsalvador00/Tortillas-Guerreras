import { describe, expect, it } from 'vitest';
import { alertaExistencia, resumenCierre } from './reglas';


describe('alertas de existencias', () => {
  it('marca caducado, por caducar y bajo mínimo', () => {
    const hoy = '2026-11-03';
    expect(alertaExistencia({ cantidad: 5, fechaCaducidad: '2026-11-02', stockMinimo: null }, hoy)).toBe('caducado');
    expect(alertaExistencia({ cantidad: 5, fechaCaducidad: '2026-11-05', stockMinimo: null }, hoy)).toBe('por-caducar');
    expect(alertaExistencia({ cantidad: 5, fechaCaducidad: '2026-11-10', stockMinimo: 10 }, hoy)).toBe('bajo-minimo');
    expect(alertaExistencia({ cantidad: 0, fechaCaducidad: '2026-11-01', stockMinimo: null }, hoy)).toBeNull();
    expect(alertaExistencia({ cantidad: 50, fechaCaducidad: null, stockMinimo: 10 }, hoy)).toBeNull();
  });
});

describe('cierre de conteo (RN-INV-09)', () => {
  it('resume los ajustes de entrada y salida que se generarán', () => {
    expect(resumenCierre([{ diferencia: -3 }, { diferencia: 1 }, { diferencia: 0 }])).toEqual({ partidas: 2, entradas: 1, salidas: 1 });
  });
});
