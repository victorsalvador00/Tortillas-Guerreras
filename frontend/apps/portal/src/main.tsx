import './estilos.css';
import { configurarCliente } from '@tg/api-client';
import { ProveedorTema } from '@tg/ui';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { Portal } from './Portal';

// Portal público: sin sesión; el API limita las solicitudes por IP (RN-FAC-08).
configurarCliente({ urlBase: import.meta.env.VITE_API_URL ?? '', obtenerToken: () => null });

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ProveedorTema>
      <Portal />
    </ProveedorTema>
  </StrictMode>,
);
