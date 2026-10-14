// @vitest-environment jsdom
import { act, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { TablaDatos } from './tabla';

const columnas = [{ header: 'Folio', accessorKey: 'folio' }];

describe('TablaDatos', () => {
  it('no entra en un ciclo de renders cuando los datos pasan a undefined al cambiar un filtro', async () => {
    const { rerender } = render(<TablaDatos columnas={columnas} datos={[{ folio: 'C-1' }]} />);
    // Cambio de filtro: la consulta nueva todavía no tiene datos.
    await act(async () => rerender(<TablaDatos columnas={columnas} datos={undefined} cargando />));
    await act(async () => rerender(<TablaDatos columnas={columnas} datos={[]} mensajeVacio="Sin cargas" />));
    expect(screen.getByText('Sin cargas')).toBeTruthy();
  });
});
