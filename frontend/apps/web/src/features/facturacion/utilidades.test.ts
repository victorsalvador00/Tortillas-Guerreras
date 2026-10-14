import type { VentaDto } from '@tg/api-client';
import { describe, expect, it, vi } from 'vitest';

vi.mock('@tg/api-client', () => ({ clienteApi: vi.fn() }));

const { metodoPago, totalesSeleccion, ventasFacturables } = await import('./utilidades');

function venta(id: string, extra: Partial<VentaDto> = {}): VentaDto {
  return {
    id,
    folio: `VTA-${id}`,
    origen: 'Web',
    tipo: 'VentaRuta',
    clienteId: 'c1',
    cliente: 'Abarrotes Lupita',
    almacenId: 'a1',
    vendedorId: 'u1',
    fechaHoraServidor: '2026-11-03T16:00:00Z',
    fechaOperativa: '2026-11-03',
    condicionPago: 'Contado',
    formaPago: 'Efectivo',
    subtotal: 0.1,
    ieps: 0,
    iva: 0.2,
    total: 0.3,
    requiereRevision: false,
    estado: 'Registrada',
    partidas: [],
    ...extra,
  } as VentaDto;
}

describe('factura desde ventas', () => {
  it('RN-FAC-01: solo ventas registradas sin factura vigente', () => {
    const ventas = [venta('1'), venta('2', { facturaId: 'f1' }), venta('3', { estado: 'Cancelada' })];
    expect(ventasFacturables(ventas).map((v) => v.id)).toEqual(['1']);
  });

  it('suma la selección en centavos sin errores de punto flotante', () => {
    const ventas = [venta('1'), venta('2'), venta('3')];
    const t = totalesSeleccion(ventas, new Set(['1', '2']));
    expect(t.ventas).toBe(2);
    expect(t.subtotal).toBe(0.2);
    expect(t.total).toBe(0.6);
  });

  it('RN-FAC-07: PPD si alguna venta es a crédito', () => {
    expect(metodoPago([venta('1')])).toBe('PUE');
    expect(metodoPago([venta('1'), venta('2', { condicionPago: 'Credito' })])).toBe('PPD');
  });
});
