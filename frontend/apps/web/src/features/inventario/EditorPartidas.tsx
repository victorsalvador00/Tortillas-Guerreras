import { Boton, BotonIcono, Campo, Selector, cn } from '@tg/ui';
import { SelectorProducto } from '@/componentes/comunes';
import { nuevaPartida, type PartidaCaptura } from './partidas';

/**
 * Editor de partidas de inventario (ajustes y traspasos): producto, lote, cantidad y, si aplica, sentido
 * Entrada/Salida, costo y caducidad de las entradas.
 */
export function EditorPartidas({
  partidas,
  onCambiar,
  errores,
  conSentido = false,
  sentidoNuevo = 'Salida',
}: {
  partidas: PartidaCaptura[];
  onCambiar: (partidas: PartidaCaptura[]) => void;
  errores: Record<string, string>;
  conSentido?: boolean;
  sentidoNuevo?: PartidaCaptura['sentido'];
}) {
  const actualizar = (clave: string, cambios: Partial<PartidaCaptura>) =>
    onCambiar(partidas.map((p) => (p.clave === clave ? { ...p, ...cambios } : p)));

  return (
    <div className="flex flex-col gap-3">
      {partidas.map((p, i) => {
        const entrada = conSentido && p.sentido === 'Entrada';
        return (
          <div
            key={p.clave}
            className={cn(
              'grid items-start gap-3 rounded-xl border border-row-divider p-3',
              conSentido ? 'md:grid-cols-[32px_189px_minmax(0,1fr)_202px_189px_auto]' : 'md:grid-cols-[32px_minmax(0,1fr)_202px_189px_auto]',
            )}
          >
            <span className="pt-3 text-sm text-muted">{i + 1}</span>
            {conSentido && (
              <Selector
                etiqueta="Movimiento"
                compacto
                opciones={[
                  { valor: 'Entrada', etiqueta: 'Entrada (+)' },
                  { valor: 'Salida', etiqueta: 'Salida (−)' },
                ]}
                value={p.sentido}
                onChange={(e) => actualizar(p.clave, { sentido: e.target.value as PartidaCaptura['sentido'] })}
              />
            )}
            <SelectorProducto
              compacto
              valor={p.producto?.id}
              producto={p.producto}
              onCambiar={(producto) => actualizar(p.clave, { producto, lote: producto?.manejaLote ? p.lote : '' })}
              error={errores[`${p.clave}.producto`]}
            />
            <Campo
              etiqueta={p.producto?.manejaLote ? (entrada ? 'Lote' : 'Lote (vacío = FEFO)') : 'Sin lote'}
              compacto
              placeholder={p.producto?.manejaLote ? (entrada ? 'Lote' : 'FEFO') : 'Sin lote'}
              disabled={!p.producto?.manejaLote}
              value={p.lote}
              onChange={(e) => actualizar(p.clave, { lote: e.target.value.toUpperCase() })}
              error={errores[`${p.clave}.lote`]}
            />
            <Campo
              etiqueta="Cantidad"
              compacto
              type="number"
              inputMode="decimal"
              step="any"
              min="0"
              placeholder="Cantidad"
              sufijo={p.producto?.unidadBaseClave}
              value={p.cantidad}
              onChange={(e) => actualizar(p.clave, { cantidad: e.target.value })}
              error={errores[`${p.clave}.cantidad`]}
            />
            <BotonIcono
              icono="delete"
              etiqueta={`Quitar partida ${i + 1}`}
              className="mt-0.5"
              onClick={() => onCambiar(partidas.filter((x) => x.clave !== p.clave))}
            />
            {entrada && (
              <div className="grid gap-3 sm:grid-cols-2 md:col-start-3 md:col-end-5">
                <Campo
                  etiqueta="Costo unitario"
                  compacto
                  type="number"
                  inputMode="decimal"
                  step="any"
                  placeholder="Costo unitario (sin IVA)"
                  prefijo="$"
                  value={p.costo}
                  onChange={(e) => actualizar(p.clave, { costo: e.target.value })}
                  error={errores[`${p.clave}.costo`]}
                />
                {p.producto?.manejaLote && (
                  <Campo
                    etiqueta="Caducidad"
                    compacto
                    type="date"
                    value={p.caducidad}
                    onChange={(e) => actualizar(p.clave, { caducidad: e.target.value })}
                  />
                )}
              </div>
            )}
          </div>
        );
      })}
      {errores.general && <p className="m-0 text-sm text-error">{errores.general}</p>}
      <div>
        <Boton variante="secundario" icono="add" onClick={() => onCambiar([...partidas, nuevaPartida(sentidoNuevo)])}>
          Agregar partida
        </Boton>
      </div>
    </div>
  );
}
