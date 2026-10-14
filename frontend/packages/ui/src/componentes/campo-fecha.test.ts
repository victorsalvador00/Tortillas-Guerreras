import { describe, expect, it } from 'vitest';
import { dmyAIso, isoADmy, mascaraDmy } from './campo-fecha';

describe('fechas en DD/MM/AAAA', () => {
  it('convierte ISO a DD/MM/AAAA y de regreso', () => {
    expect(isoADmy('2026-11-03')).toBe('03/11/2026');
    expect(isoADmy('2026-11-03T06:00:00Z')).toBe('03/11/2026');
    expect(dmyAIso('03/11/2026')).toBe('2026-11-03');
    expect(isoADmy('')).toBe('');
  });

  it('rechaza fechas que no existen o incompletas', () => {
    expect(dmyAIso('31/02/2026')).toBeNull();
    expect(dmyAIso('13/13/2026')).toBeNull();
    expect(dmyAIso('3/11/2026')).toBeNull();
    expect(dmyAIso('29/02/2028')).toBe('2028-02-29');
  });

  it('inserta las diagonales mientras se escribe', () => {
    expect(mascaraDmy('03')).toBe('03');
    expect(mascaraDmy('0311')).toBe('03/11');
    expect(mascaraDmy('03112026')).toBe('03/11/2026');
    expect(mascaraDmy('03/11/20261')).toBe('03/11/2026');
  });
});
