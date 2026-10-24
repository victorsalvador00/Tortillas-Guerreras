import { mensajeError, useGenerarCargasSugeridas, useObtenerCargasSugeridas } from '@tg/api-client';
import { formatoCantidad, formatoFecha } from '@tg/shared';
import { Aviso, Boton, Casilla, Dialogo, EstadoVacio, toast } from '@tg/ui';
import { useEffect, useState } from 'react';

/**
 * RN-LOG-14: una carga inicial por ruta con sus pedidos confirmados sin carga; si la ruta ya tiene una carga inicial
 * Preparado, los pedidos se le agregan. Después se revisa y despacha como cualquier carga.
 */
export function DialogoCargasSugeridas({ fecha, onCerrar }: { fecha: string; onCerrar: () => void }) {
  const { data, isLoading, error } = useObtenerCargasSugeridas({ fecha });
  const generar = useGenerarCargasSugeridas();
  const [elegidas, setElegidas] = useState<Set<string>>(new Set());
  const [errorGenerar, setErrorGenerar] = useState<string | null>(null);
  const rutas = data?.rutas ?? [];
  useEffect(() => setElegidas(new Set((data?.rutas ?? []).map((r) => r.rutaId))), [data]);

  const enviar = async () => {
    setErrorGenerar(null);
    try {
      const r = await generar.mutateAsync({ data: { fecha, rutaIds: [...elegidas] } });
      if (r.generadas.length > 0) toast.success(`${r.generadas.length} carga(s) listas: ${r.generadas.map((g) => g.folio).join(', ')}.`);
      if (r.omitidas.length > 0) {
        setErrorGenerar(r.omitidas.map((o) => `${o.rutaCodigo}: ${o.mensaje}`).join(' · '));
        return;
      }
      onCerrar();
    } catch (err) {
      setErrorGenerar(mensajeError(err));
    }
  };

  return (
    <Dialogo
      abierto
      onCambiar={(a) => !a && onCerrar()}
      titulo="Cargas sugeridas"
      descripcion={`Pedidos confirmados sin carga con entrega hasta el ${formatoFecha(fecha)}.`}
      ancho="grande"
      acciones={
        <>
          <Boton variante="texto" onClick={onCerrar}>
            Cerrar
          </Boton>
          <Boton icono="local_shipping" disabled={elegidas.size === 0} cargando={generar.isPending} onClick={enviar}>
            Generar ({elegidas.size})
          </Boton>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        {(error || errorGenerar) && <Aviso tipo="error">{errorGenerar ?? mensajeError(error)}</Aviso>}
        {isLoading ? (
          <span className="tg-skeleton block h-32" />
        ) : rutas.length === 0 ? (
          <EstadoVacio icono="inventory_2" mensaje="No hay pedidos confirmados pendientes de cargar." />
        ) : (
          rutas.map((r) => (
            <section key={r.rutaId} className="rounded-card border border-card-border p-3">
              <Casilla
                etiqueta={`${r.rutaCodigo} · ${r.rutaNombre}`}
                descripcion={
                  r.envioFolio
                    ? `Se agrega a la carga ${r.envioFolio} (Preparado).`
                    : `Nueva carga inicial · ${r.pedidos.length} pedido(s): ${r.pedidos.map((p) => p.folio).join(', ')}`
                }
                checked={elegidas.has(r.rutaId)}
                onChange={(e) =>
                  setElegidas((s) => {
                    const n = new Set(s);
                    if (e.target.checked) n.add(r.rutaId);
                    else n.delete(r.rutaId);
                    return n;
                  })
                }
              />
              <ul className="m-0 mt-2 grid list-none gap-x-4 gap-y-1 p-0 pl-7 text-sm sm:grid-cols-2">
                {r.partidas.map((p) => (
                  <li key={p.productoId} className="flex justify-between gap-2 tabular-nums">
                    <span className="truncate" title={p.producto}>
                      {p.sku}
                    </span>
                    <span>
                      {formatoCantidad(p.cantidadBase)} {p.unidad}
                      {p.cambioFisicoBase > 0 && <span className="text-xs text-muted"> ({formatoCantidad(p.cambioFisicoBase)} cambio)</span>}
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          ))
        )}
      </div>
    </Dialogo>
  );
}
