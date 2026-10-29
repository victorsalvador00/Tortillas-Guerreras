import { useNavigate } from '@tanstack/react-router';
import { useListarOrdenesCompra, type EstadoOrdenCompra, type OrdenCompraResumenDto } from '@tg/api-client';
import { formatoFecha, formatoMoneda } from '@tg/shared';
import { Boton, Campo, EtiquetaEstado, Pestanas, Selector, TablaDatos, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros, exportarCsv } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { ESTADOS_OC, SelectorProveedor } from './comunes';

/** Estados que agrupa cada pestaña; «todas» no filtra (salvo el selector de estado). */
const ESTADOS_PESTANA: Record<string, EstadoOrdenCompra[]> = {
  todas: [],
  porAprobar: ['Borrador'],
  porRecibir: ['Autorizada', 'RecibidaParcial'],
};

function Contador({ valor }: { valor: number | undefined }) {
  return (
    <span className="ml-2 rounded-full bg-soft px-2.5 py-0.5 text-xs font-medium text-primary">{valor ?? '–'}</span>
  );
}

export function OrdenesCompra() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const [pestana, setPestana] = useState('todas');
  const [estado, setEstado] = useState('');
  const [proveedorId, setProveedorId] = useState('');
  const [desde, setDesde] = useState('');
  const [hasta, setHasta] = useState('');
  const [buscar, setBuscar] = useState('');
  const [pagina, setPagina] = useState(1);
  const estados = pestana === 'todas' ? (estado ? [estado as EstadoOrdenCompra] : []) : ESTADOS_PESTANA[pestana];
  const { data, isLoading, isFetching } = useListarOrdenesCompra({
    estado: estados.length ? estados : undefined,
    proveedorId: proveedorId || undefined,
    desde: desde || undefined,
    hasta: hasta || undefined,
    buscar: buscar || undefined,
    pagina,
    tamano: 50,
  });
  // Los contadores muestran todo lo pendiente, sin importar los filtros.
  const porAprobar = useListarOrdenesCompra({ estado: ESTADOS_PESTANA.porAprobar, tamano: 1 });
  const porRecibir = useListarOrdenesCompra({ estado: ESTADOS_PESTANA.porRecibir, tamano: 1 });

  const columnas = useMemo<ColumnDef<OrdenCompraResumenDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Proveedor', accessorKey: 'proveedor' },
      {
        header: 'Fecha esperada',
        accessorKey: 'fechaEntregaEsperada',
        cell: ({ row }) => formatoFecha(row.original.fechaEntregaEsperada),
        meta: { ocultarEnMovil: true },
      },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
      { header: 'Líneas', accessorKey: 'partidas', meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Total', accessorKey: 'total', cell: ({ row }) => formatoMoneda(row.original.total), meta: { alinear: 'fin' } },
    ],
    [],
  );

  const reiniciar = <T,>(fijar: (v: T) => void) => (v: T) => {
    fijar(v);
    setPagina(1);
  };

  const contenido = (
    <>
      <BarraFiltros>
        <Campo
          etiqueta="Buscar por folio o proveedor"
          type="search"
          value={buscar}
          onChange={(e) => reiniciar(setBuscar)(e.target.value)}
        />
        {pestana === 'todas' && (
          <Selector etiqueta="Estado" vacio="Todos" opciones={ESTADOS_OC} value={estado} onChange={(e) => reiniciar(setEstado)(e.target.value)} />
        )}
        <SelectorProveedor vacio="Todos" value={proveedorId} onChange={reiniciar(setProveedorId)} />
        <div className="grid grid-cols-2 gap-3">
          <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => reiniciar(setDesde)(e.target.value)} />
          <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => reiniciar(setHasta)(e.target.value)} />
        </div>
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(o) => o.id}
        alClicFila={(o) => navegar({ to: '/compras/ordenes/$id', params: { id: o.id } })}
        mensajeVacio={
          pestana === 'porAprobar'
            ? 'No hay órdenes por aprobar.'
            : pestana === 'porRecibir'
              ? 'No hay órdenes pendientes de recibir.'
              : 'No hay órdenes de compra con esos filtros.'
        }
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </>
  );

  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Órdenes de compra"
        subtitulo="Captura, autorización y seguimiento de lo recibido de cada orden."
        acciones={
          <>
            <Boton
              variante="secundario"
              icono="download"
              disabled={!data?.items.length}
              onClick={() =>
                exportarCsv('ordenes-compra', data?.items ?? [], [
                  { titulo: 'Folio', valor: (o) => o.folio },
                  { titulo: 'Fecha', valor: (o) => o.fecha },
                  { titulo: 'Proveedor', valor: (o) => o.proveedor },
                  { titulo: 'Estado', valor: (o) => o.estado },
                  { titulo: 'Total', valor: (o) => o.total },
                ])
              }
            >
              Exportar CSV
            </Boton>
            {tienePermiso('compras.editar') && (
              <Boton icono="add" onClick={() => navegar({ to: '/compras/ordenes/nueva' })}>
                Nueva orden
              </Boton>
            )}
          </>
        }
      />
      <Pestanas
        estirar
        valor={pestana}
        onCambiar={reiniciar(setPestana)}
        pestanas={[
          { valor: 'todas', etiqueta: 'Todas', contenido },
          {
            valor: 'porAprobar',
            etiqueta: (
              <>
                Por aprobar
                <Contador valor={porAprobar.data?.total} />
              </>
            ),
            contenido,
          },
          {
            valor: 'porRecibir',
            etiqueta: (
              <>
                Por recibir
                <Contador valor={porRecibir.data?.total} />
              </>
            ),
            contenido,
          },
        ]}
      />
    </Pagina>
  );
}
