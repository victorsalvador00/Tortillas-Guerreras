import type { AnyRoute } from '@tanstack/react-router';
import { Link } from '@tanstack/react-router';
import { Icono, Tarjeta } from '@tg/ui';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { MENU } from '@/app/menu';
import { pantalla } from '@/app/rutas-base';
import { useSesion } from '@/app/sesion';
import { Reportes } from './Reportes';
import { Tablero } from './Tablero';

/** Inicio para usuarios sin tablero: accesos directos a lo que su rol puede usar. */
function Bienvenida() {
  const { perfil, tienePermiso } = useSesion();
  const opciones = MENU.flatMap((s) => s.opciones).filter((o) => o.ruta !== '/' && tienePermiso(o.permiso));
  return (
    <Pagina>
      <EncabezadoPagina titulo={`Hola, ${perfil?.nombreCompleto ?? ''}`} subtitulo="Elige a dónde quieres ir." />
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {opciones.map((o) => (
          <Link key={o.ruta} to={o.ruta} className="no-underline">
            <Tarjeta className="flex h-full items-center gap-3 text-on-surface hover:bg-row-hover">
              <Icono nombre={o.icono} className="text-primary" />
              <span className="font-medium">{o.etiqueta}</span>
            </Tarjeta>
          </Link>
        ))}
      </div>
    </Pagina>
  );
}

function Inicio() {
  const { tienePermiso } = useSesion();
  return tienePermiso('tablero.ver') ? <Tablero /> : <Bienvenida />;
}

export function rutasTablero(): AnyRoute[] {
  return [pantalla('/', Inicio), pantalla('/reportes', Reportes, ['tablero.ver', 'ventas.ver', 'produccion.ver', 'inventario.ver', 'logistica.ver'])];
}
