import type { LiquidacionPartidaDto } from '@tg/api-client';
import { describe, expect, it } from 'vitest';
import { capturasIniciales, diferencia, llavePartida, resumenCierre } from './liquidacion';

function partida(lote: string, esperado: number, extra: Partial<LiquidacionPartidaDto> = {}): LiquidacionPartidaDto {
  return {
    productoId: 'p1',
    sku: 'PT-TORT-KG',
    producto: 'Tortilla',
    unidad: 'KG',
    lote,
    cargado: 300,
    vendido: 260,
    devueltoClientesVendible: 0,
    devueltoClientesNoVendible: 5,
    sobranteEsperado: esperado,
    destinoSobrante: 'Reintegrar',
    ...extra,
  };
}

describe('liquidación de ruta', () => {
  it('RN-LOG-06: diferencia = contado − esperado (criterio: esperado 40, contado 44 → +4)', () => {
    expect(diferencia(40, '44')).toBe(4);
    expect(diferencia(40, '39.5')).toBe(-0.5);
    expect(diferencia(40, '')).toBeNull();
  });

  it('RN-LOG-07: resume reintegro, merma y ajustes por diferencia', () => {
    const partidas = [partida('A', 40), partida('B', 10), partida('C', 0)];
    const capturas = capturasIniciales(partidas);
    capturas[llavePartida(partidas[0])] = { contado: '44', destino: 'Reintegrar', motivoMermaId: '' };
    capturas[llavePartida(partidas[1])] = { contado: '8', destino: 'Merma', motivoMermaId: 'm' };

    const r = resumenCierre(partidas, capturas);

    expect(r.sinContar).toBe(0);
    expect(r.reintegrar).toBe(44);
    expect(r.merma).toBe(8);
    expect(r.sobrantes).toBe(4);
    expect(r.faltantes).toBe(2);
    expect(r.conDiferencia).toBe(2);
  });

  it('cuenta las partidas con esperado distinto de cero sin capturar', () => {
    const partidas = [partida('A', 40)];
    expect(resumenCierre(partidas, capturasIniciales(partidas)).sinContar).toBe(1);
  });
});
