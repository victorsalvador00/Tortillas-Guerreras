import type { ListaPrecioPartidaDto } from '@tg/api-client';
import { formatoCentavos, formatoMoneda } from '@tg/shared';
import { Boton, BotonIcono, Campo, Selector } from '@tg/ui';
import { SelectorProducto } from '@/componentes/comunes';
import { calcularLineas, nuevaLinea, unidadSugerida, unidadesVenta, type LineaVenta } from './partidas';

/**
 * Partidas de pedido o venta web: producto vendible, unidad de venta, cantidad y precio de la lista del cliente
 * (RN-VEN-01). Totales en centavos con el mismo redondeo que el servidor (RN-VEN-02).
 */
export function EditorPartidasVenta({
  lineas,
  onCambiar,
  precios,
  deshabilitado,
  conTipo,
}: {
  lineas: LineaVenta[];
  onCambiar: (lineas: LineaVenta[]) => void;
  precios: Pick<ListaPrecioPartidaDto, 'productoId' | 'unidadId' | 'precioUnitario'>[] | undefined;
  deshabilitado?: boolean;
  /** Pedidos: permite partidas de cambio físico (RN-VEN-17). */
  conTipo?: boolean;
}) {
  const calculo = calcularLineas(lineas, precios);
  const cambiar = (i: number, cambio: Partial<LineaVenta>) =>
    onCambiar(lineas.map((l, j) => (j === i ? { ...l, ...cambio } : l)));

  return (
    <div className="flex flex-col gap-3">
      {lineas.length === 0 && <p className="m-0 text-sm text-muted">Agrega los productos.</p>}
      {lineas.map((l, i) => {
        const c = calculo.lineas[i];
        const sinPrecio = l.producto && l.unidadId && c.precio == null;
        return (
          <div
            key={l.clave}
            className={`grid items-start gap-2 rounded-xl border border-row-divider p-3 ${
              conTipo ? 'sm:grid-cols-[minmax(0,1fr)_150px_172px_132px_150px_auto]' : 'sm:grid-cols-[minmax(0,1fr)_202px_162px_176px_auto]'
            }`}
          >
            <SelectorProducto
              compacto
              etiqueta="Producto"
              valor={l.producto?.id}
              producto={l.producto}
              filtro={{ esVendible: true }}
              deshabilitado={deshabilitado}
              onCambiar={(p) => cambiar(i, { producto: p, unidadId: p ? unidadSugerida(p) : '' })}
            />
            <Selector
              compacto
              etiqueta="Unidad"
              vacio={l.producto ? undefined : 'Unidad'}
              opciones={unidadesVenta(l.producto)}
              value={l.unidadId}
              disabled={deshabilitado || !l.producto}
              onChange={(e) => cambiar(i, { unidadId: e.target.value })}
            />
            {conTipo && (
              <Selector
                compacto
                etiqueta="Tipo"
                opciones={[
                  { valor: 'Venta', etiqueta: 'Venta' },
                  { valor: 'CambioFisico', etiqueta: 'Cambio físico' },
                ]}
                value={l.tipo ?? 'Venta'}
                disabled={deshabilitado}
                onChange={(e) => cambiar(i, { tipo: e.target.value as LineaVenta['tipo'] })}
              />
            )}
            <Campo
              compacto
              etiqueta="Cantidad"
              type="number"
              inputMode="decimal"
              step="any"
              min="0"
              value={l.cantidad}
              disabled={deshabilitado}
              onChange={(e) => cambiar(i, { cantidad: e.target.value })}
            />
            <div className="flex min-h-11 flex-col justify-center text-right text-sm">
              <span className="text-xs text-muted">
                {l.tipo === 'CambioFisico' ? 'Cambio físico' : c.precio != null ? `${formatoMoneda(c.precio)} c/u` : 'Precio'}
              </span>
              <span className="font-medium tabular-nums">{c.importes ? formatoCentavos(c.importes.total) : '—'}</span>
            </div>
            {!deshabilitado ? (
              <BotonIcono icono="delete" etiqueta="Quitar partida" onClick={() => onCambiar(lineas.filter((_, j) => j !== i))} />
            ) : (
              <span />
            )}
            {sinPrecio && (
              <p className={`m-0 text-xs text-error ${conTipo ? 'sm:col-span-6' : 'sm:col-span-5'}`}>
                El producto no tiene precio en la lista del cliente para esa unidad (RN-VEN-01).
              </p>
            )}
          </div>
        );
      })}
      {!deshabilitado && (
        <div>
          <Boton variante="secundario" tamano="chico" icono="add" onClick={() => onCambiar([...lineas, nuevaLinea()])}>
            Agregar partida
          </Boton>
        </div>
      )}
      <ResumenTotales totales={calculo.totales} />
    </div>
  );
}

export function ResumenTotales({ totales }: { totales: { subtotal: number; ieps: number; iva: number; total: number } }) {
  return (
    <dl className="m-0 ml-auto grid w-full max-w-xs grid-cols-2 gap-x-6 gap-y-1 text-sm">
      <dt className="text-muted">Subtotal</dt>
      <dd className="m-0 text-right tabular-nums">{formatoCentavos(totales.subtotal)}</dd>
      <dt className="text-muted">IEPS</dt>
      <dd className="m-0 text-right tabular-nums">{formatoCentavos(totales.ieps)}</dd>
      <dt className="text-muted">IVA</dt>
      <dd className="m-0 text-right tabular-nums">{formatoCentavos(totales.iva)}</dd>
      <dt className="font-medium">Total</dt>
      <dd className="m-0 text-right text-base font-semibold tabular-nums">{formatoCentavos(totales.total)}</dd>
    </dl>
  );
}
