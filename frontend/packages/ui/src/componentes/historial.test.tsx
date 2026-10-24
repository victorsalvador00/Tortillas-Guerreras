// @vitest-environment jsdom
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Historial, etiquetaColumna, valorHistorial } from './historial';

describe('Historial (RN-SEG-07)', () => {
  it('muestra el valor anterior y el nuevo de cada campo que cambió, con usuario y origen', () => {
    render(
      <Historial
        formatoFecha={() => '24/10/2026 10:00'}
        etiquetaTabla={(t) => (t === 'pedidos_partidas' ? 'Partida de pedido' : t)}
        registros={[
          { id: '1', fechaHora: '2026-10-24T16:00:00Z', tabla: 'pedidos_partidas', accion: 'Cambio', cambios: { cantidad: [100, 80] }, usuario: 'Ana', origen: 'App' },
        ]}
      />,
    );
    expect(screen.getByText('Partida de pedido')).toBeTruthy();
    expect(screen.getByText('Cantidad')).toBeTruthy();
    expect(screen.getByText('100')).toBeTruthy();
    expect(screen.getByText('80')).toBeTruthy();
    expect(screen.getByText(/Ana · App/)).toBeTruthy();
  });

  it('sin registros muestra el estado vacío', () => {
    render(<Historial registros={[]} formatoFecha={(f) => f} />);
    expect(screen.getByText('Todavía no hay cambios registrados.')).toBeTruthy();
  });

  it('formatea columnas y valores en español', () => {
    expect(etiquetaColumna('cantidad_entregada_base')).toBe('Cantidad entregada base');
    expect(etiquetaColumna('cliente_id')).toBe('Cliente');
    expect(valorHistorial(true)).toBe('Sí');
    expect(valorHistorial(null)).toBe('—');
  });
});
