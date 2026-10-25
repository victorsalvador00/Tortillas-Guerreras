import { describe, expect, it } from 'vitest';
import { avanceRecepcion, pendientePartida, precioSugerido, totalesOrden, unidadesCompra } from './calculos';

describe('compras: cálculos de la orden', () => {
  it('1 TON de maíz a $6,500 sin IVA y 2 bultos de cal a $180 con IVA 16 %', () => {
    const t = totalesOrden([
      { cantidad: 1, precioUnitario: 6500, tasaIva: 0 },
      { cantidad: 2, precioUnitario: 180, tasaIva: 0.16 },
    ]);
    expect(t.subtotal).toBe(686000);
    expect(t.iva).toBe(5760);
    expect(t.total).toBe(691760);
  });

  it('ignora partidas incompletas', () => {
    expect(totalesOrden([{ cantidad: '', precioUnitario: 10, tasaIva: 0 }]).total).toBe(0);
  });

  it('calcula lo pendiente en la unidad de compra', () => {
    expect(pendientePartida({ cantidadBase: 1000, cantidadRecibidaBase: 400, factorABase: 1000 })).toBe(0.6);
    expect(pendientePartida({ cantidadBase: 100, cantidadRecibidaBase: 105, factorABase: 1 })).toBe(0);
    expect(avanceRecepcion({ cantidadBase: 1000, cantidadRecibidaBase: 400 })).toBe(40);
  });

  it('ofrece la unidad base y las conversiones de compra', () => {
    const unidades = unidadesCompra({
      unidadBaseId: 'kg',
      unidadBaseClave: 'KG',
      conversiones: [
        { unidadId: 'ton', unidadClave: 'TON', unidadNombre: 'Tonelada', factorABase: 1000, usoCompra: true, usoVenta: false, usoProduccion: false, predeterminadaVenta: false, usoTraslado: false, predeterminadaTraslado: false },
        { unidadId: 'paq', unidadClave: 'PAQ', unidadNombre: 'Paquete', factorABase: 1, usoCompra: false, usoVenta: true, usoProduccion: false, predeterminadaVenta: true, usoTraslado: false, predeterminadaTraslado: false },
      ],
    });
    expect(unidades.map((u) => u.valor)).toEqual(['kg', 'ton']);
  });
});

describe('compras: precio sugerido (RN-COM-12)', () => {
  const producto = { unidadBaseId: 'kg', conversiones: [{ unidadId: 'ton', usoCompra: true }] } as never;

  it('usa el precio pactado del catálogo del proveedor', () => {
    expect(precioSugerido(producto, { unidadId: 'ton', precio: 6500 }, { unidadId: 'kg', ultimoPrecio: 7, ultimaCompraEn: 'x' })).toEqual({
      unidadId: 'ton',
      precio: '6500',
    });
  });

  it('sin precio pactado usa el último pagado y, si no hubo compras, la unidad sin precio', () => {
    expect(precioSugerido(producto, { unidadId: 'ton', precio: null }, { unidadId: 'kg', ultimoPrecio: 7, ultimaCompraEn: 'x' })).toEqual({
      unidadId: 'kg',
      precio: '7',
    });
    expect(precioSugerido(producto, { unidadId: 'ton' }, { unidadId: 'kg', ultimoPrecio: 0, ultimaCompraEn: null })).toEqual({
      unidadId: 'ton',
      precio: '',
    });
    expect(precioSugerido(producto)).toEqual({ unidadId: 'ton', precio: '' });
  });
});
