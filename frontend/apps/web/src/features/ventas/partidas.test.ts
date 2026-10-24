import { describe, expect, it } from 'vitest';
import { calcularLineas, nuevaLinea, precioDeLista, unidadesVenta, type LineaVenta } from './partidas';

const tortilla: NonNullable<LineaVenta['producto']> = {
  id: 'tortilla',
  sku: 'PT-TORT-KG',
  nombre: 'Tortilla de maíz',
  unidadBaseId: 'kg',
  unidadBaseClave: 'KG',
  tasaIva: 0,
  tasaIeps: null,
  conversiones: [
    { unidadId: 'paq', unidadClave: 'PAQ', unidadNombre: 'Paquete', factorABase: 1, usoCompra: false, usoVenta: true, usoProduccion: false, predeterminadaVenta: true, usoTraslado: false, predeterminadaTraslado: false },
  ],
};

const totopo: NonNullable<LineaVenta['producto']> = {
  id: 'totopo',
  sku: 'PT-TOTOPO',
  nombre: 'Totopo',
  unidadBaseId: 'paq',
  unidadBaseClave: 'PAQ',
  tasaIva: 0.16,
  tasaIeps: 0.08,
  conversiones: [],
};

const precios = [
  { productoId: 'tortilla', unidadId: 'kg', precioUnitario: 22 },
  { productoId: 'totopo', unidadId: 'paq', precioUnitario: 18.5 },
];

describe('partidas de venta (RN-VEN-01 / RN-VEN-02)', () => {
  it('toma el precio de la lista por producto y unidad', () => {
    expect(precioDeLista(precios, 'tortilla', 'kg')).toBe(22);
    expect(precioDeLista(precios, 'tortilla', 'paq')).toBeNull();
  });

  it('ofrece la unidad base y las de venta', () => {
    expect(unidadesVenta(tortilla).map((u) => u.valor)).toEqual(['kg', 'paq']);
  });

  it('calcula importes y totales en centavos con IEPS antes del IVA', () => {
    const lineas: LineaVenta[] = [
      { ...nuevaLinea(), producto: tortilla, unidadId: 'kg', cantidad: '5' },
      { ...nuevaLinea(), producto: totopo, unidadId: 'paq', cantidad: '3' },
    ];
    const r = calcularLineas(lineas, precios);
    expect(r.completas).toBe(true);
    expect(r.lineas[0].importes?.total).toBe(11000);
    expect(r.lineas[1].importes).toEqual({ importe: 5550, ieps: 444, iva: 959, total: 6953 });
    expect(r.totales).toEqual({ subtotal: 16550, ieps: 444, iva: 959, total: 17953 });
  });

  it('marca incompleto si falta precio de lista o cantidad', () => {
    const sinPrecio = calcularLineas([{ ...nuevaLinea(), producto: tortilla, unidadId: 'paq', cantidad: '2' }], precios);
    expect(sinPrecio.completas).toBe(false);
    expect(sinPrecio.totales.total).toBe(0);
    const sinCantidad = calcularLineas([{ ...nuevaLinea(), producto: tortilla, unidadId: 'kg', cantidad: '' }], precios);
    expect(sinCantidad.completas).toBe(false);
    expect(calcularLineas([], precios).completas).toBe(false);
  });
});
