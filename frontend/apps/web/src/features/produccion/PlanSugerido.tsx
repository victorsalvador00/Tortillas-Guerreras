import { mensajeError, useCrearOrdenesPlanSugerido, useObtenerPlanSugerido, type PlanSugeridoFilaDto, type Turno } from '@tg/api-client';
import { fechaOperativa, formatoCantidad, formatoFechaLarga } from '@tg/shared';
import { Aviso, Boton, Campo, Casilla, Dialogo, EstadoVacio, EtiquetaEstado, Selector, TablaDatos, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { SelectorLinea, TURNOS } from './comunes';

function manana(): string {
  const d = new Date(`${fechaOperativa()}T12:00:00`);
  d.setDate(d.getDate() + 1);
  return d.toISOString().slice(0, 10);
}

/** Crear órdenes de los faltantes elegidos: una por producto con su receta vigente (PRO-008 si no tiene). */
function DialogoCrearOrdenes({ abierto, onCambiar, filas }: { abierto: boolean; onCambiar: (a: boolean) => void; filas: PlanSugeridoFilaDto[] }) {
  const crear = useCrearOrdenesPlanSugerido();
  const [dia, setDia] = useState(fechaOperativa());
  const [turno, setTurno] = useState<Turno>('Matutino');
  const [lineaId, setLineaId] = useState('');
  const [error, setError] = useState<string | null>(null);

  const enviar = async () => {
    setError(null);
    try {
      const r = await crear.mutateAsync({
        data: {
          fechaOperativa: dia,
          turno,
          lineaId: lineaId || null,
          partidas: filas.map((f) => ({ productoId: f.productoId, cantidad: f.faltante })),
        },
      });
      if (r.creadas.length > 0) toast.success(`${r.creadas.length} orden(es) planeadas: ${r.creadas.map((c) => c.folio).join(', ')}.`);
      if (r.omitidas.length > 0) {
        setError(r.omitidas.map((o) => `${filas.find((f) => f.productoId === o.productoId)?.sku ?? '?'}: ${o.mensaje}`).join(' · '));
        return;
      }
      onCambiar(false);
    } catch (err) {
      setError(mensajeError(err));
    }
  };

  return (
    <Dialogo
      abierto={abierto}
      onCambiar={onCambiar}
      titulo="Crear órdenes de producción"
      descripcion={`${filas.length} producto(s) por su faltante. Cada orden usa la receta vigente.`}
      acciones={
        <>
          <Boton variante="texto" onClick={() => onCambiar(false)}>
            Cerrar
          </Boton>
          <Boton cargando={crear.isPending} onClick={enviar}>
            Crear órdenes
          </Boton>
        </>
      }
    >
      <div className="grid gap-4 sm:grid-cols-2">
        {error && (
          <Aviso tipo="error" className="sm:col-span-2">
            {error}
          </Aviso>
        )}
        <Campo etiqueta="Fecha operativa" type="date" value={dia} onChange={(e) => setDia(e.target.value)} />
        <Selector etiqueta="Turno" opciones={TURNOS} value={turno} onChange={(e) => setTurno(e.target.value as Turno)} />
        <SelectorLinea value={lineaId} onChange={setLineaId} />
      </div>
    </Dialogo>
  );
}

/** RN-PRO-13: demanda − cargado − disponible en PT − órdenes abiertas = faltante. */
export function PlanSugerido() {
  const { tienePermiso } = useSesion();
  const [fecha, setFecha] = useState(manana());
  const [referencia, setReferencia] = useState(true);
  const [elegidos, setElegidos] = useState<Set<string>>(new Set());
  const [creando, setCreando] = useState(false);
  const { data, isLoading, error } = useObtenerPlanSugerido({ fechaEntrega: fecha, incluirReferencia: referencia });
  const filas = data?.filas ?? [];
  const puedePlanear = tienePermiso('produccion.planear');
  const seleccion = filas.filter((f) => elegidos.has(f.productoId));

  const columnas = useMemo<ColumnDef<PlanSugeridoFilaDto, unknown>[]>(() => {
    const cant = (k: keyof PlanSugeridoFilaDto) => ({
      cell: ({ row }: { row: { original: PlanSugeridoFilaDto } }) => formatoCantidad(row.original[k] as number),
      meta: { alinear: 'fin' as const },
    });
    return [
      ...(puedePlanear
        ? [
            {
              id: 'elegir',
              header: '',
              cell: ({ row }: { row: { original: PlanSugeridoFilaDto } }) =>
                row.original.accion === 'Producir' && row.original.faltante > 0 ? (
                  <Casilla
                    etiqueta=""
                    aria-label={`Elegir ${row.original.sku}`}
                    checked={elegidos.has(row.original.productoId)}
                    onChange={(e) =>
                      setElegidos((s) => {
                        const n = new Set(s);
                        if (e.target.checked) n.add(row.original.productoId);
                        else n.delete(row.original.productoId);
                        return n;
                      })
                    }
                  />
                ) : null,
            } satisfies ColumnDef<PlanSugeridoFilaDto, unknown>,
          ]
        : []),
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.producto}</span>
            <span className="text-xs text-muted">
              {row.original.sku} · {row.original.unidad}
            </span>
          </span>
        ),
      },
      {
        header: 'Acción',
        accessorKey: 'accion',
        cell: ({ row }) =>
          row.original.accion === 'Comprar' ? (
            <span className="text-sm">Surtir de compra</span>
          ) : row.original.tieneReceta ? (
            'Producir'
          ) : (
            <EtiquetaEstado estado="Sin receta" />
          ),
      },
      {
        header: 'Demanda',
        accessorKey: 'demanda',
        cell: ({ row }) => (
          <span>
            {formatoCantidad(row.original.demanda)}
            {row.original.demandaEstimada > 0 && (
              <span className="block text-xs text-muted">{formatoCantidad(row.original.demandaEstimada)} estimado</span>
            )}
          </span>
        ),
        meta: { alinear: 'fin' },
      },
      { header: 'Cargado', accessorKey: 'cargado', ...cant('cargado') },
      { header: 'Disponible PT', accessorKey: 'disponible', ...cant('disponible') },
      { header: 'En órdenes', accessorKey: 'enOrdenes', ...cant('enOrdenes') },
      {
        header: 'Faltante',
        accessorKey: 'faltante',
        cell: ({ row }) => <strong className={row.original.faltante > 0 ? 'text-primary' : ''}>{formatoCantidad(row.original.faltante)}</strong>,
        meta: { alinear: 'fin' },
      },
    ];
  }, [elegidos, puedePlanear]);

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Plan sugerido"
        subtitulo={`Entrega ${formatoFechaLarga(fecha)}`}
        migas={[{ etiqueta: 'Producción' }, { etiqueta: 'Plan sugerido' }]}
        acciones={
          puedePlanear && (
            <Boton icono="add_task" disabled={seleccion.length === 0} onClick={() => setCreando(true)}>
              Crear órdenes ({seleccion.length})
            </Boton>
          )
        }
      />
      <BarraFiltros>
        <Campo etiqueta="Fecha de entrega" type="date" value={fecha} onChange={(e) => setFecha(e.target.value || manana())} />
        <Casilla
          etiqueta="Incluir referencia"
          descripcion="Rutas sin pedido suman lo pedido hace N días."
          checked={referencia}
          onChange={(e) => setReferencia(e.target.checked)}
        />
      </BarraFiltros>
      {error ? (
        <Aviso tipo="error">{mensajeError(error)}</Aviso>
      ) : !isLoading && filas.length === 0 ? (
        <EstadoVacio icono="checklist" mensaje="No hay pedidos para esta fecha de entrega." />
      ) : (
        <TablaDatos columnas={columnas} datos={filas} idFila={(f) => f.productoId} cargando={isLoading} ordenable={false} />
      )}
      {creando && <DialogoCrearOrdenes abierto={creando} onCambiar={setCreando} filas={seleccion} />}
    </Pagina>
  );
}
