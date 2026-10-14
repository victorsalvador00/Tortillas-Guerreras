import { useAprobarDispositivo, useDesactivarDispositivo, useListarDispositivos, useListarUsuarios, type DispositivoDto } from '@tg/api-client';
import { formatoFechaHora } from '@tg/shared';
import { Boton, DialogoConfirmacion, EtiquetaEstado, Selector, TablaDatos, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';

function DialogoAprobar({ dispositivo, onCerrar }: { dispositivo: DispositivoDto; onCerrar: () => void }) {
  const aprobar = useAprobarDispositivo();
  const { data: usuarios } = useListarUsuarios({ tamano: 200 });
  const [usuarioId, setUsuarioId] = useState(dispositivo.usuarioId ?? '');
  return (
    <DialogoConfirmacion
      abierto
      onCambiar={(a) => !a && onCerrar()}
      titulo={`Aprobar ${dispositivo.nombre}`}
      mensaje="Al aprobarlo recibe su código (D01, D02…), que se usa como prefijo de los folios de venta (RN-SEG-03)."
      textoConfirmar="Aprobar"
      alConfirmar={async () => {
        const r = await aprobar.mutateAsync({ id: dispositivo.id, data: { usuarioId: usuarioId || null } });
        toast.success(`Dispositivo aprobado con código ${r.codigo}.`);
      }}
    >
      <Selector
        etiqueta="Usuario asignado"
        vacio="Quien lo registró"
        opciones={(usuarios?.items ?? []).filter((u) => u.activo).map((u) => ({ valor: u.id, etiqueta: `${u.nombreCompleto} (${u.usuario})` }))}
        value={usuarioId}
        onChange={(e) => setUsuarioId(e.target.value)}
      />
    </DialogoConfirmacion>
  );
}

export function Dispositivos() {
  const { data, isLoading, isFetching } = useListarDispositivos();
  const desactivar = useDesactivarDispositivo();
  const [aprobando, setAprobando] = useState<DispositivoDto | null>(null);
  const [desactivando, setDesactivando] = useState<DispositivoDto | null>(null);

  const columnas = useMemo<ColumnDef<DispositivoDto, unknown>[]>(
    () => [
      { header: 'Código', accessorKey: 'codigo', cell: ({ row }) => <span className="font-medium">{row.original.codigo ?? '—'}</span> },
      {
        header: 'Dispositivo',
        accessorKey: 'nombre',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.nombre}</span>
            <span className="text-xs text-muted">
              {row.original.plataforma === 'ios' ? 'iOS' : 'Android'}
              {row.original.versionApp ? ` · app ${row.original.versionApp}` : ''}
            </span>
          </span>
        ),
      },
      { header: 'Usuario', accessorKey: 'usuarioNombre', cell: ({ row }) => row.original.usuarioNombre ?? '—' },
      {
        header: 'Última sincronización',
        accessorKey: 'ultimoSyncEn',
        cell: ({ row }) => (row.original.ultimoSyncEn ? formatoFechaHora(row.original.ultimoSyncEn) : 'Nunca'),
        meta: { ocultarEnMovil: true },
      },
      {
        header: 'Pendientes',
        accessorKey: 'operacionesPendientes',
        cell: ({ row }) => (
          <span className={row.original.operacionesPendientes > 0 ? 'font-medium text-[var(--tg-status-orange-fg)]' : ''}>
            {row.original.operacionesPendientes}
          </span>
        ),
        meta: { alinear: 'fin' },
      },
      {
        header: 'Consecutivo',
        accessorKey: 'ultimoConsecutivo',
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
      {
        id: 'acciones',
        header: '',
        enableSorting: false,
        meta: { alinear: 'fin' },
        cell: ({ row }) => (
          <span className="flex justify-end gap-2">
            {!row.original.activo && (
              <Boton tamano="chico" variante="secundario" icono="verified" onClick={() => setAprobando(row.original)}>
                {row.original.estado === 'PorAprobar' ? 'Aprobar' : 'Reactivar'}
              </Boton>
            )}
            {row.original.activo && (
              <Boton tamano="chico" variante="texto" icono="block" onClick={() => setDesactivando(row.original)}>
                Desactivar
              </Boton>
            )}
          </span>
        ),
      },
    ],
    [],
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Dispositivos de ruta"
        subtitulo="Teléfonos registrados desde la app. Un dispositivo inactivo no puede refrescar su sesión ni sincronizar (RN-SEG-02)."
      />
      <TablaDatos
        columnas={columnas}
        datos={data}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(d) => d.id}
        mensajeVacio="Aún no se registra ningún dispositivo. Se registran al iniciar sesión por primera vez en la app de ruta."
      />
      {aprobando && <DialogoAprobar dispositivo={aprobando} onCerrar={() => setAprobando(null)} />}
      {desactivando && (
        <DialogoConfirmacion
          abierto
          onCambiar={(a) => !a && setDesactivando(null)}
          titulo={`Desactivar ${desactivando.codigo ?? desactivando.nombre}`}
          mensaje={`Se cierran sus sesiones y ya no podrá sincronizar.${
            desactivando.operacionesPendientes > 0
              ? ` Atención: reportó ${desactivando.operacionesPendientes} operación(es) sin enviar.`
              : ''
          }`}
          textoConfirmar="Desactivar"
          peligro
          alConfirmar={async () => {
            await desactivar.mutateAsync({ id: desactivando.id });
            toast.success('Dispositivo desactivado.');
          }}
        />
      )}
    </Pagina>
  );
}
