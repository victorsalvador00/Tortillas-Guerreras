import { useNavigate } from '@tanstack/react-router';
import { useListarOrdenesCompra, type EstadoOrdenCompra, type OrdenCompraResumenDto } from '@tg/api-client';
import { formatoFecha, formatoMoneda } from '@tg/shared';
import { Boton, Campo, EtiquetaEstado, Selector, TablaDatos, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros, exportarCsv } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { ESTADOS_OC, SelectorProveedor } from './comunes';

export function OrdenesCompra() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const [estado, setEstado] = useState('');
  const [proveedorId, setProveedorId] = useState('');
  const [desde, setDesde] = useState('');
  const [hasta, setHasta] = useState('');
  const [buscar, setBuscar] = useState('');
  const [pagina, setPagina] = useState(1);
  const { data, isLoading, isFetching } = useListarOrdenesCompra({
    estado: (estado || undefined) as EstadoOrdenCompra | undefined,
    proveedorId: proveedorId || undefined,
    desde: desde || undefined,
    hasta: hasta || undefined,
    buscar: buscar || undefined,
    pagina,
    tamano: 50,
  });

  const columnas = useMemo<ColumnDef<OrdenCompraResumenDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fecha', cell: ({ row }) => formatoFecha(row.original.fecha) },
      { header: 'Proveedor', accessorKey: 'proveedor' },
      {
        header: 'Entrega esperada',
        accessorKey: 'fechaEntregaEsperada',
        cell: ({ row }) => formatoFecha(row.original.fechaEntregaEsperada),
        meta: { ocultarEnMovil: true },
      },
      { header: 'Partidas', accessorKey: 'partidas', meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Total', accessorKey: 'total', cell: ({ row }) => formatoMoneda(row.original.total), meta: { alinear: 'fin' } },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );

  const reiniciar = <T,>(fijar: (v: T) => void) => (v: T) => {
    fijar(v);
    setPagina(1);
  };

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
      <BarraFiltros>
        <Campo etiqueta="Buscar folio" type="search" value={buscar} onChange={(e) => reiniciar(setBuscar)(e.target.value)} />
        <Selector etiqueta="Estado" vacio="Todos" opciones={ESTADOS_OC} value={estado} onChange={(e) => reiniciar(setEstado)(e.target.value)} />
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
        mensajeVacio="No hay órdenes de compra con esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </Pagina>
  );
}
