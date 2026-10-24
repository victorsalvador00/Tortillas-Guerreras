import { EstadoVacio, EtiquetaEstado, Girador, type ColorEstado } from './basicos';

/** Fila de la bitácora (RN-SEG-06) tal como la devuelve `GET /seguridad/bitacora`. */
export interface RegistroHistorial {
  id: string;
  fechaHora: string;
  tabla: string;
  accion: string;
  /** Cambio: `{ columna: [antes, después] }`; Alta: `{ columna: valor }`; Baja: `{ columna: valor }` (o `[antes, después]` si es baja lógica). */
  cambios: unknown;
  usuario?: string | null;
  origen: string;
}

const COLOR_ACCION: Record<string, ColorEstado> = { Alta: 'verde', Cambio: 'azul', Baja: 'rojo' };

// Columnas técnicas que no aportan al leer el historial.
const OCULTAS = new Set(['id', 'version_sync', 'xmin']);

/** `cantidad_entregada_base` → "Cantidad entregada base". */
export function etiquetaColumna(columna: string): string {
  const texto = columna.replace(/_id$/, '').replace(/_/g, ' ');
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}

export function valorHistorial(valor: unknown): string {
  if (valor === null || valor === undefined || valor === '') return '—';
  if (typeof valor === 'boolean') return valor ? 'Sí' : 'No';
  if (typeof valor === 'object') return JSON.stringify(valor);
  return String(valor);
}

function Cambios({ accion, cambios }: { accion: string; cambios: unknown }) {
  const filas = Object.entries((cambios ?? {}) as Record<string, unknown>).filter(([c]) => !OCULTAS.has(c));
  if (filas.length === 0) return null;
  const lista = (
    <dl className="m-0 grid grid-cols-[minmax(8rem,auto)_1fr] gap-x-4 gap-y-1 text-sm">
      {filas.map(([columna, valor]) => (
        <div key={columna} className="contents">
          <dt className="text-muted">{etiquetaColumna(columna)}</dt>
          <dd className="m-0 break-words">
            {Array.isArray(valor) && valor.length === 2 ? (
              <>
                <span className="text-muted line-through">{valorHistorial(valor[0])}</span>
                <span aria-label="cambió a"> → </span>
                <span className="font-medium">{valorHistorial(valor[1])}</span>
              </>
            ) : (
              valorHistorial(valor)
            )}
          </dd>
        </div>
      ))}
    </dl>
  );
  // Un alta trae todos los campos: se muestra plegada.
  if (accion === 'Alta') {
    return (
      <details>
        <summary className="cursor-pointer text-sm text-primary">Ver datos capturados ({filas.length})</summary>
        <div className="mt-2">{lista}</div>
      </details>
    );
  }
  return lista;
}

/**
 * Pestaña "Historial" (RN-SEG-07): lista cronológica de altas, cambios y bajas de un documento y sus partidas.
 * Sin estado: la pantalla consulta la bitácora y le pasa los registros.
 */
export function Historial({
  registros,
  cargando,
  formatoFecha,
  etiquetaTabla,
}: {
  registros: readonly RegistroHistorial[] | undefined;
  cargando?: boolean;
  formatoFecha: (iso: string) => string;
  /** Nombre legible de cada tabla (`pedidos_partidas` → "Partida"); sin él se muestra la tabla. */
  etiquetaTabla?: (tabla: string) => string;
}) {
  if (cargando) return <Girador etiqueta="Cargando historial" />;
  if (!registros?.length) return <EstadoVacio icono="history" mensaje="Todavía no hay cambios registrados." />;
  return (
    <ol className="m-0 flex list-none flex-col gap-3 p-0">
      {registros.map((r) => (
        <li key={r.id} className="rounded-xl border border-row-divider p-3">
          <div className="mb-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
            <EtiquetaEstado etiqueta={r.accion} color={COLOR_ACCION[r.accion] ?? 'gris'} />
            <span className="font-medium">{etiquetaTabla ? etiquetaTabla(r.tabla) : r.tabla}</span>
            <span className="text-muted">
              {formatoFecha(r.fechaHora)} · {r.usuario ?? 'Sistema'} · {r.origen}
            </span>
          </div>
          <Cambios accion={r.accion} cambios={r.cambios} />
        </li>
      ))}
    </ol>
  );
}
