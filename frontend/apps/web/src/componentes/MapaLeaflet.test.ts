import { describe, expect, it } from 'vitest';
import { coordenadaValida } from './MapaLeaflet';

describe('coordenadaValida', () => {
  it('acepta coordenadas dentro del rango', () => {
    expect(coordenadaValida(19.18, -96.14)).toBe(true);
    expect(coordenadaValida(-90, 180)).toBe(true);
  });

  it('rechaza valores fuera de rango que congelarían el mapa', () => {
    expect(coordenadaValida(-9922369532567788, -96.14)).toBe(false);
    expect(coordenadaValida(19.18, -961)).toBe(false);
    expect(coordenadaValida(Number.NaN, 0)).toBe(false);
  });
});
