// @vitest-environment jsdom
import { act, fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { Campo } from './campos';

function Filtro() {
  const [fecha, setFecha] = useState('2026-10-13');
  return (
    <>
      <Campo etiqueta="Fecha operativa" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
      <output data-testid="valor">{fecha}</output>
    </>
  );
}

describe('EntradaFecha controlada', () => {
  it('elegir un día en el calendario actualiza el valor', async () => {
    render(<Filtro />);
    console.log('render ok');
    await act(async () => fireEvent.click(screen.getByLabelText('Abrir calendario')));
    console.log('abierto');
    await act(async () => fireEvent.click(screen.getByLabelText('20 de octubre de 2026')));
    console.log('elegido');
    expect(screen.getByTestId('valor').textContent).toBe('2026-10-20');
  });
});
