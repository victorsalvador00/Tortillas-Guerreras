import { mensajeError, useCancelarCambioPendiente, useListarCambiosPendientes, type CambioPendienteDto, type EstadoCambioPendiente } from '@tg/api-client';
import { formatoCantidad, formatoFecha } from '@tg/shared';
import { Aviso, Boton, Campo, Dialogo, EtiquetaEstado, Selector, TablaDatos, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { SelectorRuta } from './comunes-logistica';

const ESTADOS: { valor: EstadoCambioPendiente; etiqueta: string }[] = [
  { valor: 'Pendiente', etiqueta: 'Pendiente' },
  { valor: 'Saldado', etiqueta: 'Saldado' },
  { valor: 'Cancelado', etiqueta: 'Cancelado' },
];

function DialogoCancelar({ pendiente, onCerrar }: { pendiente: CambioPendienteDto; onCerrar: () => void }) {
  const cancelar = useCancelarCambioPendiente();
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  return (
    <Dialogo
      abierto
      onCambiar={(a) => !a && onCerrar()}
      titulo="Cancelar cambio pendiente"
      descripcion={`${pendiente.cliente}: ${formatoCantidad(pendiente.restanteBase)} ${pendiente.unidad} de ${pendiente.sku}. Ya no se le deberá.`}
      acciones={
        <>
          <Boton variante="texto" onClick={onCerrar}>
            Volver
          </Boton>
          <Boton
            variante="peligro"
            cargando={cancelar.isPending}
            onClick={async () => {
              setError(null);
              try {
                await cancelar.mutateAsync({ id: pendiente.id, data: { motivo } });
                toast.success('Cambio pendiente cancelado.');
                onCerrar();
              } catch (err) {
                setError(mensajeError(err));
              }
            }}
          >
            Cancelar pendiente
          </Boton>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Campo etiqueta="Motivo" value={motivo} onChange={(e) => setMotivo(e.target.value)} placeholder="Cambio no autorizado, resuelto en tienda…" />
      </div>
    </Dialogo>
  );
}

/** RN-LOG-16/17: lo que se le debe a cada cliente por cambios físicos; se salda al entregar en un cambio posterior. */
export function CambiosPendientes() {
  const { tienePermiso } = useSesion();
  const [rutaId, setRutaId] = useState('');
  const [estado, setEstado] = useState<EstadoCambioPendiente | ''>('Pendiente');
  const [cancelando, setCancelando] = useState<CambioPendienteDto | null>(null);
  const { data, isLoading } = useListarCambiosPendientes({ rutaId: rutaId || undefined, estado: estado || undefined });
  const puedeCancelar = tienePermiso('logistica.cambios_cancelar');

  const columnas = useMemo<ColumnDef<CambioPendienteDto, unknown>[]>(
    () => [
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Cliente', accessorKey: 'cliente' },
      { header: 'Ruta', accessorKey: 'rutaCodigo', meta: { ocultarEnMovil: true } },
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.producto}</span>
            <span className="text-xs text-muted">{row.original.sku}</span>
          </span>
        ),
      },
      {
        header: 'Pendiente',
        accessorKey: 'restanteBase',
        cell: ({ row }) => (
          <span className="tabular-nums">
            {formatoCantidad(row.original.restanteBase)} {row.original.unidad}
            {row.original.cantidadSaldadaBase > 0 && (
              <span className="block text-xs text-muted">de {formatoCantidad(row.original.cantidadBase)}</span>
            )}
          </span>
        ),
        meta: { alinear: 'fin' },
      },
      { header: 'Origen', accessorKey: 'retornoFolio', cell: ({ row }) => row.original.retornoFolio ?? '—', meta: { ocultarEnMovil: true } },
      {
        header: 'Estado',
        accessorKey: 'estado',
        cell: ({ row }) => (
          <span title={row.original.motivoCancelacion ?? undefined}>
            <EtiquetaEstado estado={row.original.estado} />
          </span>
        ),
      },
      ...(puedeCancelar
        ? [
            {
              id: 'acciones',
              header: '',
              cell: ({ row }: { row: { original: CambioPendienteDto } }) =>
                row.original.estado === 'Pendiente' ? (
                  <Boton variante="texto" tamano="chico" onClick={() => setCancelando(row.original)}>
                    Cancelar
                  </Boton>
                ) : null,
            } satisfies ColumnDef<CambioPendienteDto, unknown>,
          ]
        : []),
    ],
    [puedeCancelar],
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Cambios pendientes"
        subtitulo="Producto que se le debe a un cliente por un cambio físico. Se salda al entregarlo en un cambio posterior."
        migas={[{ etiqueta: 'Logística' }, { etiqueta: 'Cambios pendientes' }]}
      />
      <BarraFiltros>
        <SelectorRuta vacio="Todas" value={rutaId} onChange={setRutaId} />
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={ESTADOS}
          value={estado}
          onChange={(e) => setEstado(e.target.value as EstadoCambioPendiente | '')}
        />
      </BarraFiltros>
      <TablaDatos columnas={columnas} datos={data ?? []} idFila={(p) => p.id} cargando={isLoading} />
      {cancelando && <DialogoCancelar pendiente={cancelando} onCerrar={() => setCancelando(null)} />}
    </Pagina>
  );
}
