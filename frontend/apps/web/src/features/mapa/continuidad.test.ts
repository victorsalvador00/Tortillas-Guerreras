import type { ClienteRutaDto, VentaDto } from '@tg/api-client';
import { describe, expect, it } from 'vitest';
import { diaSemana } from '../logistica/comunes-logistica';
import { continuidadVisitas } from './continuidad';

const cliente = (id: string, orden: number, dias: number[], esGenerico = false): ClienteRutaDto => ({
  clienteId: id,
  codigo: id,
  nombre: `Cliente ${id}`,
  ordenVisita: orden,
  diasVisita: dias,
  esGenerico,
});

const venta = (clienteId: string, estado = 'Registrada'): VentaDto => ({ id: `v-${clienteId}`, clienteId, estado }) as VentaDto;

describe('continuidad de visitas', () => {
  it('el 3 de noviembre de 2026 es martes (2)', () => {
    expect(diaSemana('2026-11-03')).toBe(2);
    expect(diaSemana('2026-11-08')).toBe(7);
  });

  it('separa programados visitados, no visitados y fuera de programa', () => {
    const clientes = [cliente('A', 1, [1, 2, 3]), cliente('B', 2, [2]), cliente('C', 3, [5]), cliente('G', 4, [1, 2, 3, 4, 5, 6, 7], true)];
    const ventas = [venta('A'), venta('C'), venta('G'), venta('B', 'Cancelada')];

    const r = continuidadVisitas(clientes, ventas, 2);

    expect(r.programados).toBe(2);
    expect(r.visitadosProgramados).toBe(1);
    expect(r.noVisitados.map((v) => v.cliente.clienteId)).toEqual(['B']);
    expect(r.fueraDePrograma).toBe(1);
    expect(r.visitas.map((v) => v.cliente.clienteId)).toEqual(['A', 'B', 'C', 'G']);
  });
});
