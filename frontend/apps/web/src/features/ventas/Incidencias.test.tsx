import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { Incidencias } from './CobrosIncidencias';

vi.mock('@tanstack/react-router', () => ({
  useRouterState: () => '/ventas/incidencias',
  useNavigate: () => vi.fn(),
  useParams: () => ({}),
  Link: ({ children }: { children: ReactNode }) => <a>{children}</a>,
}));

vi.mock('@/app/sesion', () => ({
  useSesion: () => ({ tienePermiso: () => true, perfil: null }),
}));

vi.mock('@tg/api-client', () => ({
  mensajeError: (e: unknown) => String(e),
  useListarIncidencias: () => ({
    isLoading: false,
    isFetching: false,
    data: {
      items: [
        {
          id: 'i1',
          tipo: 'GpsFueraDeRango',
          entidadTipo: 'Venta',
          entidadId: 'v1',
          referencia: 'R01-D03-000123',
          detalle: { distanciaM: 400 },
          estado: 'Abierta',
          creadoEn: '2026-11-03T21:42:10Z',
        },
      ],
      total: 1,
      pagina: 1,
      tamano: 50,
    },
  }),
  useRevisarIncidencia: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useListarCobros: () => ({ data: undefined }),
  useObtenerSaldoCliente: () => ({ data: undefined }),
  useRegistrarCobro: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useListarAlmacenes: () => ({ data: [] }),
  listarClientes: vi.fn(),
}));

describe('Incidencias', () => {
  it('muestra la incidencia con su tipo en español, documento y acción de revisar', () => {
    render(<Incidencias />);
    expect(screen.getByRole('heading', { name: 'Incidencias' })).toBeInTheDocument();
    expect(screen.getAllByText('GPS fuera de rango').length).toBeGreaterThan(0);
    expect(screen.getByText('Venta R01-D03-000123')).toBeInTheDocument();
    expect(screen.getByText('distanciaM: 400')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Revisar' })).toBeInTheDocument();
  });
});
