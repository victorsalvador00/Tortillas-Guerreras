import { describe, expect, it } from 'vitest';
import { globosMenu, textoGlobo } from './pendientes';

describe('RN-WEB-01 globos del menú', () => {
  it('suma lo que espera en cada ruta y arma el detalle', () => {
    const globos = globosMenu({
      ordenesCompraPorAutorizar: 2,
      ordenesCompraPorRecibir: 1,
      planPorProgramar: 0,
      ordenesPorLiberar: null,
      ordenesPorProducir: 3,
      ordenesEnProceso: 0,
      liquidacionesPorConfirmar: null,
      liquidacionesPorRevisar: null,
      liquidacionesPorCerrar: null,
      pedidosPorValidar: null,
      incidenciasAbiertas: null,
      clientesPorAprobar: null,
      solicitudesAutofactura: null,
    });
    expect(globos.get('/compras/ordenes')).toEqual({ total: 3, detalle: '2 por autorizar · 1 por recibir' });
    expect(globos.get('/produccion/ordenes')).toEqual({ total: 3, detalle: '3 por producir' });
    // En cero o sin permiso (nulo) no hay globo.
    expect(globos.has('/produccion/plan-sugerido')).toBe(false);
    expect(globos.has('/ventas/pedidos')).toBe(false);
  });

  it('sin datos no hay globos y arriba de 99 se muestra 99+', () => {
    expect(globosMenu(undefined).size).toBe(0);
    expect(textoGlobo(99)).toBe('99');
    expect(textoGlobo(120)).toBe('99+');
  });
});
