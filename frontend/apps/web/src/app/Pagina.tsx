import { Link, useRouterState } from '@tanstack/react-router';
import { Aviso, EstadoVacio, Girador, Icono, cn } from '@tg/ui';
import type { ReactNode } from 'react';
import { etiquetaRuta, seccionDeRuta } from './menu';
import { useSesion } from './sesion';

export interface Miga {
  etiqueta: string;
  a?: string;
}

/** Contenedor de página: ancho máximo, márgenes y aire al final. */
export function Pagina({ children, className, ancho = 'completo' }: { children: ReactNode; className?: string; ancho?: 'completo' | 'medio' }) {
  return (
    <div className={cn('mx-auto w-full px-4 pt-4 pb-16 sm:px-6 sm:pt-6', ancho === 'completo' ? 'max-w-[1440px]' : 'max-w-4xl', className)}>
      {children}
    </div>
  );
}

/**
 * Encabezado de página: migas de pan, título, subtítulo y acciones. Las migas se calculan de la ruta salvo que se
 * pasen explícitas (p. ej. "Compras › Órdenes de compra › OC-000012").
 */
export function EncabezadoPagina({
  titulo,
  subtitulo,
  acciones,
  migas,
}: {
  titulo: ReactNode;
  subtitulo?: ReactNode;
  acciones?: ReactNode;
  migas?: Miga[];
}) {
  const ruta = useRouterState({ select: (s) => s.location.pathname });
  const calculadas: Miga[] = migas ?? [
    ...(seccionDeRuta(ruta) ? [{ etiqueta: seccionDeRuta(ruta)! }] : []),
    ...(etiquetaRuta(ruta) ? [{ etiqueta: etiquetaRuta(ruta)! }] : []),
  ];
  return (
    <div className="mb-4">
      {calculadas.length > 1 && (
        <nav aria-label="Ruta de navegación">
          <ol className="m-0 mb-1 flex list-none flex-wrap items-center p-0 text-xs text-muted">
            {calculadas.map((m, i) => (
              <li key={i} className="inline-flex items-center">
                {m.a ? (
                  <Link to={m.a} className="inline-block py-1 text-primary underline-offset-2 hover:underline">
                    {m.etiqueta}
                  </Link>
                ) : (
                  <span aria-current={i === calculadas.length - 1 ? 'page' : undefined}>{m.etiqueta}</span>
                )}
                {i < calculadas.length - 1 && <Icono nombre="chevron_right" tamano={18} />}
              </li>
            ))}
          </ol>
        </nav>
      )}
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          <h1 className="m-0 text-2xl font-semibold">{titulo}</h1>
          {subtitulo && <p className="m-0 mt-1 text-muted">{subtitulo}</p>}
        </div>
        {acciones && <div className="flex flex-wrap gap-2">{acciones}</div>}
      </div>
    </div>
  );
}

/** Protege una página por permiso (el backend también protege; aquí solo se oculta). */
export function RequierePermiso({ permiso, children }: { permiso: string | string[]; children: ReactNode }) {
  const { tienePermiso } = useSesion();
  if (!tienePermiso(permiso)) {
    return (
      <Pagina>
        <EstadoVacio icono="lock" titulo="Sin acceso" mensaje="No tienes permiso para ver esta pantalla. Pide acceso a un administrador." />
      </Pagina>
    );
  }
  return <>{children}</>;
}

/** Estados de carga y error de una consulta, con el mismo aspecto en todas las pantallas. */
export function EstadoConsulta({ cargando, error, children }: { cargando?: boolean; error?: unknown; children: ReactNode }) {
  if (cargando) {
    return (
      <div className="flex items-center justify-center gap-3 py-16 text-muted">
        <Girador etiqueta="Cargando" /> Cargando…
      </div>
    );
  }
  if (error) {
    return <Aviso tipo="error">{error instanceof Error ? error.message : 'No se pudo cargar la información.'}</Aviso>;
  }
  return <>{children}</>;
}
