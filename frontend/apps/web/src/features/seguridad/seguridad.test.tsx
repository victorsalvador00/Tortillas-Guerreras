import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { agruparPermisos, alternarGrupo } from './permisos';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: ReactNode }) => <a>{children}</a>,
  useRouterState: () => '/seguridad/dispositivos',
}));

vi.mock('@/app/Pagina', () => ({
  Pagina: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  EncabezadoPagina: ({ titulo }: { titulo: ReactNode }) => <h1>{titulo}</h1>,
}));

vi.mock('@tg/api-client', () => ({
  useListarDispositivos: () => ({
    isLoading: false,
    isFetching: false,
    data: [
      {
        id: 'd1',
        codigo: null,
        nombre: 'Moto G ruta 2',
        usuarioNombre: 'Vendedor Ruta 2',
        plataforma: 'android',
        versionApp: '1.0.0',
        ultimoSyncEn: null,
        ultimoConsecutivo: 0,
        operacionesPendientes: 0,
        activo: false,
        estado: 'PorAprobar',
      },
      {
        id: 'd2',
        codigo: 'D01',
        nombre: 'Moto G ruta 1',
        usuarioNombre: 'Vendedor Ruta 1',
        plataforma: 'android',
        versionApp: '1.0.0',
        ultimoSyncEn: '2026-11-03T16:00:00Z',
        ultimoConsecutivo: 45,
        operacionesPendientes: 3,
        activo: true,
        estado: 'Activo',
      },
    ],
  }),
  useAprobarDispositivo: () => ({ mutateAsync: vi.fn() }),
  useDesactivarDispositivo: () => ({ mutateAsync: vi.fn() }),
  useListarUsuarios: () => ({ data: { items: [] } }),
}));

const permisos = [
  { clave: 'ventas.ver', descripcion: 'Consultar ventas' },
  { clave: 'inventario.ver', descripcion: 'Consultar existencias' },
  { clave: 'inventario.ajustar', descripcion: 'Ajustar' },
  { clave: 'sync.operar', descripcion: 'Sincronizar' },
];

describe('permisos por módulo (RN-SEG-04)', () => {
  it('agrupa por prefijo en el orden del menú', () => {
    const grupos = agruparPermisos(permisos);
    expect(grupos.map((g) => g.etiqueta)).toEqual(['Inventario', 'Ventas', 'App de ruta']);
    expect(grupos[0].permisos.map((p) => p.clave)).toEqual(['inventario.ajustar', 'inventario.ver']);
  });

  it('marca y desmarca un grupo completo sin tocar los demás', () => {
    const [inventario] = agruparPermisos(permisos);
    const marcados = alternarGrupo(['ventas.ver'], inventario, true);
    expect(marcados.sort()).toEqual(['inventario.ajustar', 'inventario.ver', 'ventas.ver']);
    expect(alternarGrupo(marcados, inventario, false)).toEqual(['ventas.ver']);
  });
});

describe('Dispositivos', () => {
  it('ofrece aprobar los pendientes y desactivar los activos (RN-SEG-03)', async () => {
    const { Dispositivos } = await import('./Dispositivos');
    render(
      <QueryClientProvider client={new QueryClient()}>
        <Dispositivos />
      </QueryClientProvider>,
    );
    expect(screen.getByText('Moto G ruta 2')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Aprobar/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Desactivar/ })).toBeInTheDocument();
    expect(screen.getByText('Por aprobar')).toBeInTheDocument();
  }, 30_000);
});
