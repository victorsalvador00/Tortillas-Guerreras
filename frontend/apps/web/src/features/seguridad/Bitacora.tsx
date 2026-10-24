import { useListarBitacora, useListarUsuarios, type RegistroBitacoraDto } from '@tg/api-client';
import { formatoFechaHora } from '@tg/shared';
import { Campo, EtiquetaEstado, Historial, Selector, TablaDatos, etiquetaColumna, valorHistorial, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros } from '@/componentes/comunes';
import { TABLAS, etiquetaTabla } from '@/componentes/historial';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';

const COLOR_ACCION = { Alta: 'verde', Cambio: 'azul', Baja: 'rojo' } as const;

/** Resumen de una fila para la tabla: los primeros campos que cambiaron. */
function resumen(r: RegistroBitacoraDto): string {
  const cambios = Object.entries((r.cambios ?? {}) as Record<string, unknown>);
  const texto = cambios
    .slice(0, 3)
    .map(([c, v]) => (Array.isArray(v) && v.length === 2 ? `${etiquetaColumna(c)}: ${valorHistorial(v[0])} → ${valorHistorial(v[1])}` : etiquetaColumna(c)))
    .join(' · ');
  return cambios.length > 3 ? `${texto} · +${cambios.length - 3}` : texto;
}

/** Seguridad → Bitácora (RN-SEG-07): consulta por documento, por usuario y por fechas; no se edita ni se borra. */
export function Bitacora() {
  const [tabla, setTabla] = useState('');
  const [usuarioId, setUsuarioId] = useState('');
  const [desde, setDesde] = useState('');
  const [hasta, setHasta] = useState('');
  const [pagina, setPagina] = useState(1);
  const { data: usuarios } = useListarUsuarios({ tamano: 200 });
  const { data, isLoading, isFetching } = useListarBitacora({
    tabla: tabla || undefined,
    usuarioId: usuarioId || undefined,
    desde: desde || undefined,
    hasta: hasta || undefined,
    pagina,
  });
  const reiniciar = (set: (v: string) => void) => (v: string) => {
    set(v);
    setPagina(1);
  };

  const columnas = useMemo<ColumnDef<RegistroBitacoraDto, unknown>[]>(
    () => [
      { header: 'Fecha', accessorKey: 'fechaHora', cell: ({ row }) => formatoFechaHora(row.original.fechaHora) },
      { header: 'Registro', accessorKey: 'tabla', cell: ({ row }) => etiquetaTabla(row.original.tabla) },
      {
        header: 'Acción',
        accessorKey: 'accion',
        cell: ({ row }) => <EtiquetaEstado etiqueta={row.original.accion} color={COLOR_ACCION[row.original.accion as keyof typeof COLOR_ACCION] ?? 'gris'} />,
      },
      { header: 'Cambios', id: 'cambios', cell: ({ row }) => <span className="text-sm">{resumen(row.original)}</span>, meta: { ocultarEnMovil: true } },
      { header: 'Usuario', accessorKey: 'usuario', cell: ({ row }) => row.original.usuario ?? 'Sistema' },
      { header: 'Origen', accessorKey: 'origen', meta: { ocultarEnMovil: true } },
    ],
    [],
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Bitácora"
        subtitulo="Historial de altas, cambios y bajas con usuario, origen y valores anteriores. No se puede editar ni borrar."
      />
      <BarraFiltros>
        <Selector
          etiqueta="Registro"
          vacio="Todos"
          opciones={Object.entries(TABLAS).map(([valor, etiqueta]) => ({ valor, etiqueta }))}
          value={tabla}
          onChange={(e) => reiniciar(setTabla)(e.target.value)}
        />
        <Selector
          etiqueta="Usuario"
          vacio="Todos"
          opciones={(usuarios?.items ?? []).map((u) => ({ valor: u.id, etiqueta: u.nombreCompleto }))}
          value={usuarioId}
          onChange={(e) => reiniciar(setUsuarioId)(e.target.value)}
        />
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => reiniciar(setDesde)(e.target.value)} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => reiniciar(setHasta)(e.target.value)} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(r) => r.id}
        ordenable={false}
        detalle={(r) => <Historial registros={[r]} formatoFecha={formatoFechaHora} etiquetaTabla={etiquetaTabla} />}
        mensajeVacio="No hay cambios con esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </Pagina>
  );
}
