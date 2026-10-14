import './estilos.css';
import { MutationCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { esErrorApi, type PerfilDto } from '@tg/api-client';
import { Avisos, ProveedorTema } from '@tg/ui';
import { StrictMode, useMemo } from 'react';
import { createRoot } from 'react-dom/client';
import { crearRouter } from './app/router';
import { ProveedorSesion, restaurarSesion, useSesion } from './app/sesion';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      // Errores de negocio (4xx) no se reintentan; los de red sí, dos veces.
      retry: (intentos, error) => !(esErrorApi(error) && error.estado < 500) && intentos < 2,
    },
  },
  mutationCache: new MutationCache({
    // Tras cualquier escritura se refrescan los listados visibles.
    onSuccess: () => void queryClient.invalidateQueries(),
  }),
});

function App() {
  const sesion = useSesion();
  // El router se crea una vez; el contexto de sesión se actualiza en cada render.
  const router = useMemo(() => crearRouter(sesion), []); // eslint-disable-line react-hooks/exhaustive-deps
  return <RouterProvider router={router} context={{ sesion }} />;
}

async function iniciar() {
  let perfil: PerfilDto | null = null;
  try {
    perfil = await restaurarSesion();
  } catch {
    perfil = null;
  }
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <ProveedorTema>
        <QueryClientProvider client={queryClient}>
          <ProveedorSesion inicial={perfil}>
            <App />
            <Avisos />
          </ProveedorSesion>
        </QueryClientProvider>
      </ProveedorTema>
    </StrictMode>,
  );
}

void iniciar();
