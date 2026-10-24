import { describe, expect, it } from 'vitest';
import { alertasCantidad, pendientesCambio, precioVigente, productoPermitido, siguienteDiaVisita, textoAlerta } from './pedidos';
import { inventarioCamioneta } from './ventas';

describe('precios especiales', () => {
  const especiales = [{ productoId: 'p', unidadId: 'u', precioUnitario: 9.5, vigenteDesde: '2026-11-10' }];

  it('RN-VEN-12: el precio especial vigente gana sobre la lista; antes de su vigencia aplica la lista', () => {
    expect(precioVigente('p', 'u', '2026-11-10', especiales, 11)).toEqual({ precio: 9.5, especial: true });
    expect(precioVigente('p', 'u', '2026-11-09', especiales, 11)).toEqual({ precio: 11, especial: false });
    expect(precioVigente('p', 'otra', '2026-11-10', especiales, null)).toEqual({ precio: null, especial: false });
  });
});

describe('productos exclusivos', () => {
  const exclusivos = [
    { productoId: 'x', clienteId: 'c1', grupoClienteId: null },
    { productoId: 'x', clienteId: null, grupoClienteId: 'g1' },
  ];

  it('RN-VEN-14: solo el cliente o los del grupo listados ven el producto; sin registros lo ven todos', () => {
    expect(productoPermitido('x', { id: 'c1', grupoClienteId: null }, exclusivos)).toBe(true);
    expect(productoPermitido('x', { id: 'c2', grupoClienteId: 'g1' }, exclusivos)).toBe(true);
    expect(productoPermitido('x', { id: 'c3', grupoClienteId: 'g2' }, exclusivos)).toBe(false);
    expect(productoPermitido('x', { id: 'c3', grupoClienteId: null }, exclusivos)).toBe(false);
    expect(productoPermitido('libre', { id: 'c3', grupoClienteId: null }, exclusivos)).toBe(true);
  });
});

describe('fecha de entrega', () => {
  it('RN-VEN-15: por defecto el siguiente día de visita (nunca hoy ni antes); sin días, mañana', () => {
    // 2026-11-04 es miércoles (3).
    expect(siguienteDiaVisita('2026-11-04', [1, 3, 5])).toBe('2026-11-06');
    expect(siguienteDiaVisita('2026-11-04', [3])).toBe('2026-11-11');
    expect(siguienteDiaVisita('2026-11-07', [1])).toBe('2026-11-09');
    expect(siguienteDiaVisita('2026-11-04', [])).toBe('2026-11-05');
  });
});

describe('cantidades confiables', () => {
  const promedios = new Map([['t', 20]]);
  const partida = (cantidad: number, factorABase = 1) => ({ productoId: 't', descripcion: 'T-800', cantidad, unidadClave: 'PAQ', factorABase });

  it('RN-APP-09: capturar 200 donde el promedio es 20 pide confirmación', () => {
    const alertas = alertasCantidad([partida(200)], promedios, 3, 500);
    expect(alertas).toHaveLength(1);
    expect(textoAlerta(alertas[0])).toBe('¿Seguro? T-800: capturaste 200 PAQ (normalmente 20)');
  });

  it('RN-APP-09: dentro de N veces el promedio no alerta; el tope por partida alerta aunque no haya promedio', () => {
    expect(alertasCantidad([partida(60)], promedios, 3, 500)).toHaveLength(0);
    expect(alertasCantidad([partida(61)], promedios, 3, 500)).toHaveLength(1);
    const sinPromedio = alertasCantidad([{ ...partida(501), productoId: 'nuevo' }], promedios, 3, 500);
    expect(sinPromedio).toHaveLength(1);
    expect(sinPromedio[0].normal).toBeNull();
  });

  it('RN-APP-09: compara en unidad base aunque se capture en otra unidad', () => {
    // 2 cajas de 24 = 48 paquetes: menos de 3 × 20.
    expect(alertasCantidad([partida(2, 24)], promedios, 3, 500)).toHaveLength(0);
    const alertas = alertasCantidad([partida(3, 24)], promedios, 3, 500);
    expect(alertas[0].normal).toBe(0.83);
  });
});

describe('cambio físico', () => {
  it('RN-LOG-16: lo que se entrega de menos queda pendiente; lo que salda pendientes anteriores no cuenta', () => {
    const pendientes = pendientesCambio(
      [
        { productoId: 'a', cantidadBase: 5 },
        { productoId: 'b', cantidadBase: 2 },
      ],
      [
        { productoId: 'a', cantidadBase: 3 },
        { productoId: 'a', cantidadBase: 4, cambioPendienteId: 'viejo' },
        { productoId: 'b', cantidadBase: 2 },
      ],
    );
    expect([...pendientes]).toEqual([['a', 2]]);
  });

  it('inventario de camioneta resta lo entregado a cambio y suma lo retirado vendible', () => {
    const inv = inventarioCamioneta(
      [{ productoId: 't', cantidadBase: 100 }],
      [{ productoId: 't', cantidadBase: 10 }],
      [],
      [
        { productoId: 't', cantidadBase: 4, sentido: 'Entregado', condicion: 'Vendible' },
        { productoId: 't', cantidadBase: 3, sentido: 'Retirado', condicion: 'NoVendible' },
        { productoId: 't', cantidadBase: 1, sentido: 'Retirado', condicion: 'Vendible' },
      ],
    );
    expect(inv.get('t')).toEqual({ productoId: 't', cargado: 100, vendido: 10, devuelto: 1, cambios: 4, existencia: 87 });
  });
});
