import { Link, Outlet, useNavigate, useRouterState } from '@tanstack/react-router';
import { fechaOperativa, formatoFechaLarga } from '@tg/shared';
import { BotonIcono, Icono, Logo, MenuAcciones, OPCIONES_TEMA, cn, useTema } from '@tg/ui';
import { useEffect, useState } from 'react';
import { MENU } from './menu';
import { useSesion } from './sesion';

const CLAVE_CONTRAIDO = 'tg-menu-contraido';

function useEsMovil(): boolean {
  const consulta = '(max-width: 959.98px)';
  const [movil, setMovil] = useState(() => window.matchMedia?.(consulta).matches ?? false);
  useEffect(() => {
    const m = window.matchMedia?.(consulta);
    if (!m) return;
    const alCambiar = (e: MediaQueryListEvent) => setMovil(e.matches);
    m.addEventListener('change', alCambiar);
    return () => m.removeEventListener('change', alCambiar);
  }, []);
  return movil;
}

/**
 * Estructura de la app: barra superior de color, menú lateral con etiquetas (contraíble a 72 px; en celular se
 * superpone) y contenido sobre fondo lavanda — el mismo diseño del SGO.
 */
export function Shell() {
  const { perfil, cerrar, tienePermiso } = useSesion();
  const { modo, fijar } = useTema();
  const navegar = useNavigate();
  const ruta = useRouterState({ select: (s) => s.location.pathname });
  const movil = useEsMovil();
  const [contraido, setContraido] = useState(() => localStorage.getItem(CLAVE_CONTRAIDO) === '1');
  const [abiertoMovil, setAbiertoMovil] = useState(false);
  const mostrarContraido = !movil && contraido;

  useEffect(() => setAbiertoMovil(false), [ruta]);

  const alternarMenu = () => {
    if (movil) {
      setAbiertoMovil((a) => !a);
    } else {
      const nuevo = !contraido;
      setContraido(nuevo);
      localStorage.setItem(CLAVE_CONTRAIDO, nuevo ? '1' : '0');
    }
  };

  const secciones = MENU.map((s) => ({ ...s, opciones: s.opciones.filter((o) => tienePermiso(o.permiso)) })).filter(
    (s) => s.opciones.length > 0,
  );

  const activa = (destino: string) => (destino === '/' ? ruta === '/' : ruta === destino || ruta.startsWith(`${destino}/`));

  return (
    <div className="flex h-full flex-col">
      <a
        href="#contenido"
        className="absolute -top-24 left-2 z-[100] rounded-lg bg-primary px-3 py-2 text-on-primary focus:top-2"
      >
        Saltar al contenido
      </a>

      <header
        role="banner"
        className="flex h-[var(--tg-topbar-height)] shrink-0 items-center gap-2 bg-topbar px-2 text-topbar-ink sm:px-4"
      >
        <BotonIcono
          icono={!movil && contraido ? 'menu_open' : 'menu'}
          etiqueta={movil ? 'Abrir menú' : contraido ? 'Expandir menú' : 'Contraer menú'}
          aria-expanded={movil ? abiertoMovil : !contraido}
          plano
          className="text-topbar-ink"
          onClick={alternarMenu}
        />
        <Link to="/" className="flex min-h-12 items-center gap-3 text-inherit no-underline" aria-label="Guerreras, ir al tablero">
          <Logo chico className="h-10 drop-shadow-[0_1px_2px_rgb(0_0_0/35%)]" titulo="" />
          <span className="hidden text-[20px] font-semibold sm:inline">Sistema de Gestión</span>
        </Link>
        <span className="flex-1" />
        <span
          className="hidden items-center gap-2 rounded-full bg-white px-4 py-2 text-sm text-[#1a1b1f] md:inline-flex dark:bg-[#10141d] dark:text-topbar-ink"
          title="Fecha operativa (hora de México)"
        >
          <Icono nombre="calendar_today" tamano={18} />
          {formatoFechaLarga(fechaOperativa())}
        </span>
        <MenuAcciones
          disparador={<BotonIcono icono="account_circle" etiqueta={`Menú de ${perfil?.nombreCompleto ?? 'usuario'}`} plano className="text-topbar-ink" />}
          encabezado={
            <div className="flex flex-col">
              <strong>{perfil?.nombreCompleto}</strong>
              <span className="text-[13px] text-muted">
                {perfil?.usuario} · {perfil?.roles.join(', ')}
              </span>
            </div>
          }
          opciones={[
            'separador',
            { titulo: 'Tema' },
            ...OPCIONES_TEMA.map((t) => ({
              etiqueta: t.etiqueta,
              icono: t.icono,
              marcada: modo === t.modo,
              alSeleccionar: () => fijar(t.modo),
            })),
            'separador',
            { etiqueta: 'Cambiar contraseña', icono: 'key', alSeleccionar: () => void navegar({ to: '/perfil' }) },
            {
              etiqueta: 'Cerrar sesión',
              icono: 'logout',
              alSeleccionar: async () => {
                await cerrar();
                await navegar({ to: '/login' });
              },
            },
          ]}
        />
      </header>

      <div className="relative flex min-h-0 flex-1">
        {movil && abiertoMovil && (
          <div aria-hidden className="fixed inset-0 top-[var(--tg-topbar-height)] z-30 bg-[var(--tg-scrim)]" onClick={() => setAbiertoMovil(false)} />
        )}
        <aside
          className={cn(
            'shrink-0 overflow-y-auto border-r border-outline-variant bg-[var(--tg-sidebar-bg)] py-2 [scrollbar-width:thin] transition-[width,transform] duration-150 motion-reduce:transition-none',
            movil
              ? 'fixed top-[var(--tg-topbar-height)] bottom-0 left-0 z-40 w-[var(--tg-sidebar-width)] shadow-xl'
              : mostrarContraido
                ? 'w-[var(--tg-sidebar-collapsed-width)]'
                : 'w-[var(--tg-sidebar-width)]',
            movil && !abiertoMovil && '-translate-x-full',
          )}
          aria-hidden={movil && !abiertoMovil ? true : undefined}
        >
          <nav aria-label="Menú principal" className={cn(mostrarContraido && 'tg-nav-collapsed')}>
            {secciones.map((seccion, i) => (
              <div key={seccion.etiqueta}>
                {mostrarContraido ? (
                  i > 0 && <hr className="mx-3 my-2 border-0 border-t border-outline-variant" />
                ) : (
                  <h2 className="mx-4 mt-4 mb-1 text-xs font-medium tracking-[0.04em] text-[var(--tg-sidebar-muted)] uppercase">
                    {seccion.etiqueta}
                  </h2>
                )}
                <ul className="m-0 list-none px-2">
                  {seccion.opciones.map((o) => (
                    <li key={o.ruta}>
                      <Link
                        to={o.ruta}
                        className="tg-nav-item"
                        aria-current={activa(o.ruta) ? 'page' : undefined}
                        aria-label={mostrarContraido ? o.etiqueta : undefined}
                        title={mostrarContraido ? o.etiqueta : undefined}
                      >
                        <span className="tg-nav-tag" aria-hidden>
                          <span className="tg-nav-tag-block" />
                          <span className="tg-nav-tag-dots" />
                        </span>
                        <Icono nombre={o.icono} />
                        {!mostrarContraido && <span className="tg-nav-label text-sm leading-tight">{o.etiqueta}</span>}
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </nav>
        </aside>

        <main id="contenido" tabIndex={-1} className="min-w-0 flex-1 overflow-y-auto bg-page outline-none">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
