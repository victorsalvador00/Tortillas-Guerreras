import { render, screen } from '@testing-library/react';
import { ProveedorTema } from '@tg/ui';
import { describe, expect, it, vi } from 'vitest';
import { Portal } from './Portal';

vi.mock('@tg/api-client', async (original) => ({
  ...(await original<Record<string, unknown>>()),
  captchaPortal: vi.fn(),
  consultarTicketPortal: vi.fn(),
}));

describe('Portal de autofactura', () => {
  it('muestra el primer paso con el folio tomado de /f/<folio> del QR', () => {
    window.history.replaceState(null, '', '/f/R01-D03-000123');
    window.matchMedia = vi.fn().mockReturnValue({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() });
    render(
      <ProveedorTema>
        <Portal />
      </ProveedorTema>,
    );
    expect(screen.getByRole('heading', { name: 'Factura tu compra' })).toBeInTheDocument();
    expect(screen.getByLabelText('Folio del ticket')).toHaveValue('R01-D03-000123');
  });
});
