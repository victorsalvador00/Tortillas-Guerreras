import { useNavigate, useParams } from '@tanstack/react-router';
import {
  useAutorizarOrdenCompra,
  useCancelarOrdenCompra,
  useCerrarOrdenCompra,
  useListarRecepciones,
  useObtenerOrdenCompra,
  type PartidaOrdenCompraDto,
  type RecepcionResumenDto,
} from '@tg/api-client';
import { formatoCantidad, formatoFecha, formatoFechaHora, formatoMoneda } from '@tg/shared';
import { Boton, Dato, Datos, DialogoConfirmacion, EtiquetaEstado, TablaDatos, Tarjeta, toast, type ColumnDef } from '@tg/ui';
import { useMemo, useState } from 'react';
import { BotonRastrear } from '@/features/inventario/Trazabilidad';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { avanceRecepcion } from './calculos';

type Accion = 'autorizar' | 'cancelar' | 'cerrar';

const TEXTOS: Record<Accion, { titulo: string; mensaje: string; boton: string; peligro?: boolean }> = {
  autorizar: {
    titulo: 'Autorizar orden de compra',
    mensaje: 'Una vez autorizada ya no se edita y se puede recibir contra ella.',
    boton: 'Autorizar',
  },
  cancelar: {
    titulo: 'Cancelar orden de compra',
    mensaje: 'Solo se cancela si no tiene recepciones. Esta acción no se puede deshacer.',
    boton: 'Cancelar orden',
    peligro: true,
  },
  cerrar: {
    titulo: 'Cerrar con faltante',
    mensaje: 'La orden queda cerrada con lo recibido hasta ahora y ya no admite recepciones.',
    boton: 'Cerrar orden',
    peligro: true,
  },
};

export function OrdenCompraDetalle() {
  const { id } = useParams({ strict: false }) as { id: string };
  const navegar = useNavigate();
  const { tienePermiso } = useSesion();
  const { data: orden, isLoading, error } = useObtenerOrdenCompra(id);
  const recepciones = useListarRecepciones({ ordenCompraId: id, tamano: 100 });
  const autorizar = useAutorizarOrdenCompra();
  const cancelar = useCancelarOrdenCompra();
  const cerrar = useCerrarOrdenCompra();
  const [accion, setAccion] = useState<Accion | null>(null);

  const columnas = useMemo<ColumnDef<PartidaOrdenCompraDto, unknown>[]>(
    () => [
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
        header: 'Pedido',
        accessorKey: 'cantidad',
        cell: ({ row }) => `${formatoCantidad(row.original.cantidad)} ${row.original.unidad}`,
        meta: { alinear: 'fin' },
      },
      { header: 'Precio', accessorKey: 'precioUnitario', cell: ({ row }) => formatoMoneda(row.original.precioUnitario), meta: { alinear: 'fin', ocultarEnMovil: true } },
      { header: 'Importe', accessorKey: 'importe', cell: ({ row }) => formatoMoneda(row.original.importe), meta: { alinear: 'fin' } },
      { header: 'IVA', accessorKey: 'iva', cell: ({ row }) => formatoMoneda(row.original.iva), meta: { alinear: 'fin', ocultarEnMovil: true } },
      {
        header: 'Recibido',
        id: 'recibido',
        cell: ({ row }) => {
          const p = row.original;
          const avance = avanceRecepcion(p);
          return (
            <span className="inline-flex min-w-36 flex-col items-end gap-1">
              <span className="tabular-nums">
                {formatoCantidad(p.cantidadRecibidaBase)} / {formatoCantidad(p.cantidadBase)} {p.unidadBase}
              </span>
              <span className="h-1.5 w-28 overflow-hidden rounded-full bg-container-high" aria-hidden>
                <span
                  className={avance >= 100 ? 'block h-full bg-[var(--tg-status-green-fg)]' : 'block h-full bg-primary'}
                  style={{ width: `${avance}%` }}
                />
              </span>
            </span>
          );
        },
        meta: { alinear: 'fin' },
      },
    ],
    [],
  );

  const columnasRecepciones = useMemo<ColumnDef<RecepcionResumenDto, unknown>[]>(
    () => [
      { header: 'Folio', accessorKey: 'folio', cell: ({ row }) => <span className="font-medium">{row.original.folio}</span> },
      { header: 'Fecha', accessorKey: 'fechaOperativa', cell: ({ row }) => formatoFecha(row.original.fechaOperativa) },
      { header: 'Factura', accessorKey: 'facturaProveedor', cell: ({ row }) => row.original.facturaProveedor ?? '—' },
      { header: 'Estado', accessorKey: 'estado', cell: ({ row }) => <EtiquetaEstado estado={row.original.estado} /> },
    ],
    [],
  );

  const ejecutar = async (a: Accion) => {
    if (a === 'autorizar') await autorizar.mutateAsync({ id });
    if (a === 'cancelar') await cancelar.mutateAsync({ id });
    if (a === 'cerrar') await cerrar.mutateAsync({ id });
    toast.success(a === 'autorizar' ? 'Orden autorizada.' : a === 'cancelar' ? 'Orden cancelada.' : 'Orden cerrada con faltante.');
  };

  const autoriza = tienePermiso('compras.autorizar');
  const recibible = orden?.estado === 'Autorizada' || orden?.estado === 'RecibidaParcial';

  return (
    <Pagina>
      <EstadoConsulta cargando={isLoading} error={error}>
        {orden && (
          <>
            <EncabezadoPagina
              titulo={
                <span className="flex flex-wrap items-center gap-3">
                  {orden.folio} <EtiquetaEstado estado={orden.estado} />
                </span>
              }
              subtitulo={orden.proveedor}
              migas={[{ etiqueta: 'Compras' }, { etiqueta: 'Órdenes de compra', a: '/compras/ordenes' }, { etiqueta: orden.folio }]}
              acciones={
                <>
                  {orden.estado !== 'Borrador' && <BotonRastrear tipo="OrdenCompra" id={orden.id} etiqueta={orden.folio} />}
                  {orden.estado === 'Borrador' && tienePermiso('compras.editar') && (
                    <Boton variante="secundario" icono="edit" onClick={() => navegar({ to: '/compras/ordenes/$id/editar', params: { id } })}>
                      Editar
                    </Boton>
                  )}
                  {(orden.estado === 'Borrador' || orden.estado === 'Autorizada') && autoriza && (
                    <Boton variante="secundario" icono="cancel" onClick={() => setAccion('cancelar')}>
                      Cancelar
                    </Boton>
                  )}
                  {orden.estado === 'RecibidaParcial' && autoriza && (
                    <Boton variante="secundario" icono="lock" onClick={() => setAccion('cerrar')}>
                      Cerrar con faltante
                    </Boton>
                  )}
                  {orden.estado === 'Borrador' && autoriza && (
                    <Boton icono="verified" onClick={() => setAccion('autorizar')}>
                      Autorizar
                    </Boton>
                  )}
                  {recibible && tienePermiso('compras.recibir') && (
                    <Boton icono="move_to_inbox" onClick={() => navegar({ to: '/compras/recepciones/nueva', search: { oc: id } })}>
                      Registrar recepción
                    </Boton>
                  )}
                </>
              }
            />
            <div className="flex flex-col gap-4">
              <Tarjeta>
                <Datos>
                  <Dato etiqueta="Fecha">{formatoFecha(orden.fecha)}</Dato>
                  <Dato etiqueta="Entrega esperada">{formatoFecha(orden.fechaEntregaEsperada)}</Dato>
                  <Dato etiqueta="Entrega en">{orden.almacenDestino}</Dato>
                  <Dato etiqueta="Autorizada">{orden.autorizadaEn ? formatoFechaHora(orden.autorizadaEn) : '—'}</Dato>
                  <Dato etiqueta="Subtotal">{formatoMoneda(orden.subtotal)}</Dato>
                  <Dato etiqueta="IVA">{formatoMoneda(orden.iva)}</Dato>
                  <Dato etiqueta="Total">{formatoMoneda(orden.total)}</Dato>
                  {orden.notas && <Dato etiqueta="Notas">{orden.notas}</Dato>}
                </Datos>
              </Tarjeta>
              <section>
                <h2 className="m-0 mb-2 text-base font-medium">Partidas</h2>
                <TablaDatos columnas={columnas} datos={orden.partidas} idFila={(p) => p.id} ordenable={false} />
              </section>
              <section>
                <h2 className="m-0 mb-2 text-base font-medium">Recepciones</h2>
                <TablaDatos
                  columnas={columnasRecepciones}
                  datos={recepciones.data?.items}
                  cargando={recepciones.isLoading}
                  idFila={(r) => r.id}
                  alClicFila={(r) => navegar({ to: '/compras/recepciones/$id', params: { id: r.id } })}
                  mensajeVacio="Todavía no hay recepciones de esta orden."
                />
              </section>
            </div>
            {accion && (
              <DialogoConfirmacion
                abierto
                onCambiar={(a) => !a && setAccion(null)}
                titulo={TEXTOS[accion].titulo}
                mensaje={TEXTOS[accion].mensaje}
                textoConfirmar={TEXTOS[accion].boton}
                peligro={TEXTOS[accion].peligro}
                alConfirmar={() => ejecutar(accion)}
              />
            )}
          </>
        )}
      </EstadoConsulta>
    </Pagina>
  );
}
