import {
  flexRender,
  getCoreRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type SortingState,
} from '@tanstack/react-table';
import { useState, type ReactNode } from 'react';
import { cn } from '../utils';
import { BotonIcono, EstadoVacio, Icono } from './basicos';

declare module '@tanstack/react-table' {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  interface ColumnMeta<TData, TValue> {
    alinear?: 'inicio' | 'fin' | 'centro';
    /** Se oculta en pantallas angostas. */
    ocultarEnMovil?: boolean;
    className?: string;
  }
}

export interface PaginacionTabla {
  pagina: number;
  tamano: number;
  total: number;
  onCambiar: (pagina: number) => void;
}

export interface TablaDatosProps<T> {
  columnas: ColumnDef<T, unknown>[];
  datos: T[] | undefined;
  cargando?: boolean;
  /** Muestra una barra de carga sobre la tabla cuando ya hay datos (recarga). */
  actualizando?: boolean;
  mensajeVacio?: string;
  accionVacio?: ReactNode;
  alClicFila?: (fila: T) => void;
  idFila?: (fila: T) => string;
  paginacion?: PaginacionTabla;
  /** Contenido expandible por fila. */
  detalle?: (fila: T) => ReactNode;
  className?: string;
  /** Ordenamiento local por columnas. */
  ordenable?: boolean;
  pie?: ReactNode;
}

/**
 * Tabla de listados: marco redondeado tipo tarjeta, renglones amplios con divisores suaves, renglones alternos,
 * esqueleto de carga, estado vacío y paginación (mismo estilo que las pantallas del SGO).
 */
export function TablaDatos<T>({
  columnas,
  datos,
  cargando,
  actualizando,
  mensajeVacio = 'No hay registros.',
  accionVacio,
  alClicFila,
  idFila,
  paginacion,
  detalle,
  className,
  ordenable = true,
  pie,
}: TablaDatosProps<T>) {
  const [orden, setOrden] = useState<SortingState>([]);
  const [expandidas, setExpandidas] = useState<Record<string, boolean>>({});
  const tabla = useReactTable({
    data: datos ?? [],
    columns: columnas,
    state: { sorting: orden },
    onSortingChange: setOrden,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: ordenable ? getSortedRowModel() : undefined,
    getRowId: idFila ? (f) => idFila(f) : undefined,
  });
  const filas = tabla.getRowModel().rows;
  const totalColumnas = columnas.length + (detalle ? 1 : 0);

  return (
    <div className={className}>
      <div
        className="relative overflow-hidden rounded-card border border-card-border bg-card shadow-card"
        aria-busy={cargando || actualizando || undefined}
      >
        {actualizando && filas.length > 0 && (
          <div aria-hidden className="absolute inset-x-0 top-0 h-[3px] animate-[tg-pulse_1s_ease-in-out_infinite] bg-primary" />
        )}
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-sm">
            <thead>
              {tabla.getHeaderGroups().map((grupo) => (
                <tr key={grupo.id} className="h-[52px] border-b border-row-divider">
                  {detalle && <th className="w-12" aria-label="Detalle" />}
                  {grupo.headers.map((h) => {
                    const meta = h.column.columnDef.meta;
                    const puedeOrdenar = ordenable && h.column.getCanSort() && h.column.columnDef.enableSorting !== false;
                    const sentido = h.column.getIsSorted();
                    return (
                      <th
                        key={h.id}
                        scope="col"
                        aria-sort={sentido === 'asc' ? 'ascending' : sentido === 'desc' ? 'descending' : undefined}
                        className={cn(
                          'px-4 text-left text-[13px] font-medium whitespace-nowrap text-muted',
                          meta?.alinear === 'fin' && 'text-right',
                          meta?.alinear === 'centro' && 'text-center',
                          meta?.ocultarEnMovil && 'hidden md:table-cell',
                          meta?.className,
                        )}
                      >
                        {h.isPlaceholder ? null : puedeOrdenar ? (
                          <button
                            type="button"
                            onClick={h.column.getToggleSortingHandler()}
                            className={cn('inline-flex items-center gap-1 hover:text-on-surface', meta?.alinear === 'fin' && 'flex-row-reverse')}
                          >
                            {flexRender(h.column.columnDef.header, h.getContext())}
                            <Icono
                              nombre={sentido === 'desc' ? 'arrow_downward' : 'arrow_upward'}
                              tamano={16}
                              className={sentido ? 'opacity-100' : 'opacity-0'}
                            />
                          </button>
                        ) : (
                          flexRender(h.column.columnDef.header, h.getContext())
                        )}
                      </th>
                    );
                  })}
                </tr>
              ))}
            </thead>
            <tbody>
              {cargando && filas.length === 0 &&
                Array.from({ length: 5 }, (_, i) => (
                  <tr key={`esqueleto-${i}`} className="h-16 border-b border-row-divider last:border-0">
                    <td colSpan={totalColumnas} className="px-4">
                      <span className="tg-skeleton" />
                    </td>
                  </tr>
                ))}
              {filas.map((fila) => {
                const abierta = !!expandidas[fila.id];
                return [
                  <tr
                    key={fila.id}
                    tabIndex={alClicFila ? 0 : undefined}
                    onClick={alClicFila ? () => alClicFila(fila.original) : undefined}
                    onKeyDown={alClicFila ? (e) => e.key === 'Enter' && alClicFila(fila.original) : undefined}
                    className={cn(
                      'h-16 border-b border-row-divider even:bg-row-alt hover:bg-row-hover',
                      alClicFila && 'cursor-pointer',
                      abierta && 'border-b-0',
                    )}
                  >
                    {detalle && (
                      <td className="w-12 pl-2">
                        <BotonIcono
                          icono={abierta ? 'expand_less' : 'expand_more'}
                          etiqueta={abierta ? 'Ocultar detalle' : 'Ver detalle'}
                          plano
                          tamano="chico"
                          aria-expanded={abierta}
                          onClick={(e) => {
                            e.stopPropagation();
                            setExpandidas((x) => ({ ...x, [fila.id]: !abierta }));
                          }}
                        />
                      </td>
                    )}
                    {fila.getVisibleCells().map((celda) => {
                      const meta = celda.column.columnDef.meta;
                      return (
                        <td
                          key={celda.id}
                          className={cn(
                            'px-4 py-2',
                            meta?.alinear === 'fin' && 'text-right tabular-nums',
                            meta?.alinear === 'centro' && 'text-center',
                            meta?.ocultarEnMovil && 'hidden md:table-cell',
                            meta?.className,
                          )}
                        >
                          {flexRender(celda.column.columnDef.cell, celda.getContext())}
                        </td>
                      );
                    })}
                  </tr>,
                  detalle && abierta ? (
                    <tr key={`${fila.id}-detalle`} className="border-b border-row-divider">
                      <td colSpan={totalColumnas} className="bg-container-lowest px-4 pt-2 pb-4">
                        {detalle(fila.original)}
                      </td>
                    </tr>
                  ) : null,
                ];
              })}
            </tbody>
            {pie && <tfoot>{pie}</tfoot>}
          </table>
        </div>
        {!cargando && filas.length === 0 && <EstadoVacio mensaje={mensajeVacio} accion={accionVacio} />}
      </div>
      {paginacion && paginacion.total > 0 && <Paginador {...paginacion} />}
    </div>
  );
}

export function Paginador({ pagina, tamano, total, onCambiar }: PaginacionTabla) {
  const paginas = Math.max(1, Math.ceil(total / tamano));
  const desde = (pagina - 1) * tamano + 1;
  const hasta = Math.min(total, pagina * tamano);
  return (
    <nav aria-label="Paginación" className="mt-2 flex items-center justify-end gap-2 px-2 text-[13px] text-muted">
      <span>
        {desde}–{hasta} de {total}
      </span>
      <BotonIcono icono="first_page" etiqueta="Primera página" plano disabled={pagina <= 1} onClick={() => onCambiar(1)} />
      <BotonIcono icono="chevron_left" etiqueta="Página anterior" plano disabled={pagina <= 1} onClick={() => onCambiar(pagina - 1)} />
      <BotonIcono
        icono="chevron_right"
        etiqueta="Página siguiente"
        plano
        disabled={pagina >= paginas}
        onClick={() => onCambiar(pagina + 1)}
      />
      <BotonIcono icono="last_page" etiqueta="Última página" plano disabled={pagina >= paginas} onClick={() => onCambiar(paginas)} />
    </nav>
  );
}
