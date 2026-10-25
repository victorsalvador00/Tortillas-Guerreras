import { useNavigate, useParams } from '@tanstack/react-router';
import {
  useCancelarRecepcion,
  useConsultarKardex,
  useListarRecepciones,
  useObtenerRecepcion,
  type EstadoRecepcion,
  type PartidaRecepcionDto,
  type RecepcionResumenDto,
} from '@tg/api-client';
import { formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda } from '@tg/shared';
import { Boton, Campo, Dato, Datos, DialogoConfirmacion, EtiquetaEstado, Selector, TablaDatos, Tarjeta, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BarraFiltros, TablaMovimientos, exportarCsv } from '@/componentes/comunes';
import { BotonRastrear } from '@/features/inventario/Trazabilidad';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { SelectorProveedor } from './comunes';

export function Recepciones() {
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const [estado, setEstado] = useState('');
  const [proveedorId, setProveedorId] = useState('');
  const [desde, setDesde] = useState('');
  const [hasta, setHasta] = useState('');
  const [pagina, setPagina] = useState(1);
  const { data, isLoading, isFetching } = useListarRecepciones({
    estado: (estado || undefined) as EstadoRecepcion | undefined,
    proveedorId: proveedorId || undefined,
    desde: desde || undefined,
    hasta: hasta || undefined,
    pagina,
    tamano: 50,
  });
  const columnas = useMemo<ColumnDef<RecepcionResumenDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Proveedor', accessorKey: 'proveedor' },
      {
        header: 'Orden de compra',
        accessorKey: 'ordenCompraFolio',
        cell: ({ row }) => row.original.ordenCompraFolio ?? <span className="text-muted">Compra directa</span>,
      },
      { header: 'Factura', accessorKey: 'facturaProveedor', cell: ({ row }) => row.original.facturaProveedor ?? '—', meta: { ocultarEnMovil: true } },
      { header: 'Partidas', accessorKey: 'partidas', meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Recepciones"
        subtitulo="Entradas de proveedores al almacén; cada partida genera una entrada por compra en el kardex."
        acciones={
          <>
            <Boton
              variante="secundario"
              icono="download"
              disabled={!data?.items.length}
              onClick={() =>
                exportarCsv('recepciones', data?.items ?? [], [
                  { titulo: 'Folio', valor: (r) => r.folio },
                  { titulo: 'Fecha', valor: (r) => r.fechaOperativa },
                  { titulo: 'Proveedor', valor: (r) => r.proveedor },
                  { titulo: 'Orden de compra', valor: (r) => r.ordenCompraFolio ?? '' },
                  { titulo: 'Factura', valor: (r) => r.facturaProveedor ?? '' },
                  { titulo: 'Estado', valor: (r) => r.estado },
                ])
              }
            >
              Exportar CSV
            </Boton>
            {tienePermiso('compras.recibir') && (
              <Boton icono="add" onClick={() => navegar({ to: '/compras/recepciones/nueva' })}>
                Nueva recepción
              </Boton>
            )}
          </>
        }
      />
      <BarraFiltros>
        <Selector
          etiqueta="Estado"
          vacio="Todos"
          opciones={[
            { valor: 'Registrada', etiqueta: 'Registrada' },
            { valor: 'Cancelada', etiqueta: 'Cancelada' },
          ]}
          value={estado}
          onChange={(e) => {
            setEstado(e.target.value);
            setPagina(1);
          }}
        />
        <SelectorProveedor
          vacio="Todos"
          value={proveedorId}
          onChange={(v) => {
            setProveedorId(v);
            setPagina(1);
          }}
        />
        <Campo etiqueta="Desde" type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
        <Campo etiqueta="Hasta" type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
      </BarraFiltros>
      <TablaDatos
        columnas={columnas}
        datos={data?.items}
        cargando={isLoading}
        actualizando={isFetching}
        idFila={(r) => r.id}
        alClicFila={(r) => navegar({ to: '/compras/recepciones/$id', params: { id: r.id } })}
        mensajeVacio="No hay recepciones con esos filtros."
        paginacion={data ? { pagina: data.pagina, tamano: data.tamano, total: data.total, onCambiar: setPagina } : undefined}
      />
    </Pagina>
  );
}

export function RecepcionDetalle() {
  const { id } = useParams({ strict: false }) as { id: string };
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const { data: recepcion, isLoading, error } = useObtenerRecepcion(id);
  const kardex = useConsultarKardex({ documentoTipo: 'Recepcion', documentoId: id, tamano: 200 });
  const cancelar = useCancelarRecepcion();
  const [confirmar, setConfirmar] = useState(false);

  const columnas = useMemo<ColumnDef<PartidaRecepcionDto, unknown>[]>(
    () => [
      {
        header: 'Producto',
        accessorKey: 'producto',
        cell: ({ row }) => (
          <span>
            <span className="block">{row.original.producto}</span>
            <span className="text-xs text-muted">
              {row.original.sku}
              {row.original.lote ? ` · Lote ${row.original.lote}` : ''}
              {row.original.fechaCaducidad ? ` · Caduca ${formatoFecha(row.original.fechaCaducidad)}` : ''}
            </span>
          </span>
        ),
      },
      { header: 'Cantidad', accessorKey: 'cantidad', cell: ({ row }) => `${formatoCantidad(row.original.cantidad)} ${row.original.unidad}`, meta: { alinear: 'fin' } },
      {
        header: 'En inventario',
        accessorKey: 'cantidadBase',
        cell: ({ row }) => `${formatoCantidad(row.original.cantidadBase)} ${row.original.unidadBase}`,
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
      { header: 'Precio sin IVA', accessorKey: 'precioUnitario', cell: ({ row }) => formatoMoneda(row.original.precioUnitario), meta: { alinear: 'fin' } },
      {
        header: 'Costo base',
        accessorKey: 'costoUnitarioBase',
        cell: ({ row }) => `${formatoMoneda(row.original.costoUnitarioBase)} / ${row.original.unidadBase}`,
        meta: { alinear: 'fin', ocultarEnMovil: true },
      },
    ],
    [],
  );

  return (
    <Pagina>
      <EstadoConsulta cargando={isLoading} error={error}>
        {recepcion && (
          <>
            <EncabezadoPagina
              titulo={
                <span className="flex flex-wrap items-center gap-3">
                  {recepcion.folio} <EtiquetaEstado estado={recepcion.estado} />
                </span>
              }
              subtitulo={recepcion.proveedor}
              migas={[{ etiqueta: 'Compras' }, { etiqueta: 'Recepciones', a: '/compras/recepciones' }, { etiqueta: recepcion.folio }]}
              acciones={
                <>
                  <BotonRastrear tipo="Recepcion" id={recepcion.id} etiqueta={recepcion.folio} />
                  {recepcion.ordenCompraId && (
                    <Boton
                      variante="secundario"
                      icono="receipt_long"
                      onClick={() => navegar({ to: '/compras/ordenes/$id', params: { id: recepcion.ordenCompraId! } })}
                    >
                      Ver {recepcion.ordenCompraFolio}
                    </Boton>
                  )}
                  {recepcion.estado === 'Registrada' && tienePermiso('compras.autorizar') && (
                    <Boton variante="secundario" icono="cancel" onClick={() => setConfirmar(true)}>
                      Cancelar recepción
                    </Boton>
                  )}
                </>
              }
            />
            <div className="flex flex-col gap-4">
              <Tarjeta>
                <Datos>
                  <Dato etiqueta="Fecha operativa">{formatoFecha(recepcion.fechaOperativa)}</Dato>
                  <Dato etiqueta="Almacén">{recepcion.almacen}</Dato>
                  <Dato etiqueta="Orden de compra">{recepcion.ordenCompraFolio ?? 'Compra directa'}</Dato>
                  <Dato etiqueta="Factura del proveedor">{recepcion.facturaProveedor ?? '—'}</Dato>
                  <Dato etiqueta="Subtotal">{formatoMoneda(recepcion.subtotal)}</Dato>
                  <Dato etiqueta="Registrada">{formatoFechaHora(recepcion.creadoEn)}</Dato>
                  {recepcion.canceladaEn && <Dato etiqueta="Cancelada">{formatoFechaHora(recepcion.canceladaEn)}</Dato>}
                  {recepcion.notas && <Dato etiqueta="Notas">{recepcion.notas}</Dato>}
                </Datos>
              </Tarjeta>
              <section>
                <h2 className="m-0 mb-2 text-base font-medium">Partidas</h2>
                <TablaDatos columnas={columnas} datos={recepcion.partidas} idFila={(p) => p.id} ordenable={false} />
              </section>
              <section>
                <h2 className="m-0 mb-2 text-base font-medium">Movimientos de inventario generados</h2>
                <TablaMovimientos movimientos={kardex.data?.items} cargando={kardex.isLoading} />
              </section>
            </div>
            <DialogoConfirmacion
              abierto={confirmar}
              onCambiar={setConfirmar}
              titulo="Cancelar recepción"
              mensaje="Se registra una salida de ajuste por las mismas cantidades y lotes (RN-COM-07). Si la existencia del lote ya no alcanza, no se podrá cancelar."
              textoConfirmar="Cancelar recepción"
              peligro
              alConfirmar={async () => {
                await cancelar.mutateAsync({ id });
                toast.success('Recepción cancelada.');
              }}
            />
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
