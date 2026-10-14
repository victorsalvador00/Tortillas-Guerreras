import { useNavigate, useSearch } from '@tanstack/react-router';
import {
  esErrorApi,
  mensajeError,
  useListarAlmacenes,
  useListarOrdenesCompra,
  useObtenerOrdenCompra,
  useRegistrarRecepcion,
  type OrdenCompraDto,
  type PartidaRecepcionRequest,
  type ProductoDto,
} from '@tg/api-client';
import { formatoCantidad, formatoCentavos, sumarDias, fechaOperativa } from '@tg/shared';
import { Aviso, BarraAccion, Boton, BotonIcono, Campo, Pestanas, Selector, Tarjeta, toast } from '@tg/ui';
import { useEffect, useState } from 'react';
import { SelectorAlmacen, SelectorProducto } from '@/componentes/comunes';
import { EncabezadoPagina, Pagina } from '@/app/Pagina';
import { useSesion } from '@/app/sesion';
import { pendientePartida, totalesOrden, unidadesCompra } from './calculos';
import { SelectorProveedor } from './comunes';

interface LineaOc {
  partidaId: string;
  producto: string;
  sku: string;
  unidad: string;
  pendiente: number;
  cantidad: string;
  manejaLote: boolean;
  lote: string;
  caducidad: string;
}

interface LineaDirecta {
  clave: string;
  producto: ProductoDto | null;
  unidadId: string;
  cantidad: string;
  precioUnitario: string;
  lote: string;
  caducidad: string;
}

const nuevaLinea = (): LineaDirecta => ({
  clave: crypto.randomUUID(),
  producto: null,
  unidadId: '',
  cantidad: '',
  precioUnitario: '',
  lote: '',
  caducidad: '',
});

function useErrores() {
  const [errores, setErrores] = useState<Record<string, string>>({});
  const [general, setGeneral] = useState<string | null>(null);
  const aplicar = (e: unknown) => {
    if (esErrorApi(e)) setErrores(Object.fromEntries(Object.entries(e.errores).map(([c, m]) => [c, m[0]])));
    setGeneral(mensajeError(e));
  };
  return { errores, setErrores, general, setGeneral, aplicar };
}

/** Lote y caducidad, solo para productos que manejan lote (RN-COM-09). */
function CamposLote({
  lote,
  caducidad,
  onLote,
  onCaducidad,
  error,
}: {
  lote: string;
  caducidad: string;
  onLote: (v: string) => void;
  onCaducidad: (v: string) => void;
  error?: string;
}) {
  return (
    <>
      <Campo etiqueta="Lote" value={lote} onChange={(e) => onLote(e.target.value.toUpperCase())} error={error} />
      <Campo etiqueta="Caducidad (opcional)" type="date" value={caducidad} onChange={(e) => onCaducidad(e.target.value)} />
    </>
  );
}

function ContraOrden({ ordenInicial }: { ordenInicial?: string }) {
  const navegar = useNavigate();
  const registrar = useRegistrarRecepcion();
  const autorizadas = useListarOrdenesCompra({ estado: 'Autorizada', tamano: 200 });
  const parciales = useListarOrdenesCompra({ estado: 'RecibidaParcial', tamano: 200 });
  const [ordenId, setOrdenId] = useState(ordenInicial ?? '');
  const { data: orden } = useObtenerOrdenCompra(ordenId, { query: { enabled: !!ordenId } });
  const [lineas, setLineas] = useState<LineaOc[]>([]);
  const [factura, setFactura] = useState('');
  const [notas, setNotas] = useState('');
  const [guardando, setGuardando] = useState(false);
  const { errores, setErrores, general, setGeneral, aplicar } = useErrores();

  // Precarga lo pendiente de cada partida.
  useEffect(() => {
    if (!orden) return;
    setLineas(
      orden.partidas.map((p) => ({
        partidaId: p.id,
        producto: p.producto,
        sku: p.sku,
        unidad: p.unidad,
        pendiente: pendientePartida(p),
        cantidad: String(pendientePartida(p)),
        manejaLote: p.manejaLote,
        lote: '',
        caducidad: '',
      })),
    );
  }, [orden]);

  const opciones = [...(autorizadas.data?.items ?? []), ...(parciales.data?.items ?? [])].map((o) => ({
    valor: o.id,
    etiqueta: `${o.folio} · ${o.proveedor}`,
  }));

  const cambiar = (id: string, c: Partial<LineaOc>) => setLineas((ls) => ls.map((l) => (l.partidaId === id ? { ...l, ...c } : l)));

  const guardar = async (o: OrdenCompraDto) => {
    setGeneral(null);
    const recibir = lineas.filter((l) => Number(l.cantidad) > 0);
    const e: Record<string, string> = {};
    if (recibir.length === 0) e.partidas = 'Captura al menos una cantidad recibida.';
    recibir.filter((l) => l.manejaLote && !l.lote.trim()).forEach((l) => (e[`${l.partidaId}.lote`] = 'Captura el lote.'));
    setErrores(e);
    if (Object.keys(e).length) return;
    setGuardando(true);
    try {
      const recepcion = await registrar.mutateAsync({
        data: {
          ordenCompraId: o.id,
          facturaProveedor: factura || null,
          notas: notas || null,
          partidas: recibir.map<PartidaRecepcionRequest>((l) => ({
            ordenCompraPartidaId: l.partidaId,
            cantidad: Number(l.cantidad),
            lote: l.manejaLote ? l.lote.trim() : null,
            fechaCaducidad: l.manejaLote && l.caducidad ? l.caducidad : null,
          })),
        },
      });
      toast.success(`Recepción ${recepcion.folio} registrada.`);
      await navegar({ to: '/compras/recepciones/$id', params: { id: recepcion.id } });
    } catch (err) {
      aplicar(err);
    } finally {
      setGuardando(false);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      {general && <Aviso tipo="error">{general}</Aviso>}
      <Tarjeta>
        <div className="grid gap-4 md:grid-cols-3">
          <Selector etiqueta="Orden de compra" vacio="Elige una orden autorizada" opciones={opciones} value={ordenId} onChange={(e) => setOrdenId(e.target.value)} />
          <Campo etiqueta="Factura del proveedor (opcional)" value={factura} onChange={(e) => setFactura(e.target.value)} />
          <Campo etiqueta="Notas" value={notas} onChange={(e) => setNotas(e.target.value)} />
        </div>
        {orden && (
          <p className="m-0 mt-3 text-sm text-muted">
            {orden.proveedor} · entra a {orden.almacenDestino}. Puedes recibir de más hasta la tolerancia configurada (RN-COM-04).
          </p>
        )}
      </Tarjeta>
      {orden && (
        <Tarjeta titulo="Lo que llegó" subtitulo="Se propone lo pendiente de cada partida; corrige lo que realmente se recibió.">
          {errores.partidas && <p className="m-0 mb-2 text-sm text-error">{errores.partidas}</p>}
          <div className="flex flex-col gap-3">
            {lineas.map((l, i) => (
              <div key={l.partidaId} className="grid items-start gap-3 rounded-xl border border-row-divider p-3 md:grid-cols-[2fr_1fr_1fr_1fr]">
                <div>
                  <p className="m-0 font-medium">{l.producto}</p>
                  <p className="m-0 text-xs text-muted">
                    {l.sku} · pendiente {formatoCantidad(l.pendiente)} {l.unidad}
                  </p>
                </div>
                <Campo
                  etiqueta={`Recibido (${l.unidad})`}
                  type="number"
                  step="any"
                  inputMode="decimal"
                  value={l.cantidad}
                  onChange={(e) => cambiar(l.partidaId, { cantidad: e.target.value })}
                  error={errores[`partidas[${i}].cantidad`]}
                />
                {l.manejaLote ? (
                  <CamposLote
                    lote={l.lote}
                    caducidad={l.caducidad}
                    onLote={(v) => cambiar(l.partidaId, { lote: v })}
                    onCaducidad={(v) => cambiar(l.partidaId, { caducidad: v })}
                    error={errores[`${l.partidaId}.lote`]}
                  />
                ) : (
                  <span className="hidden md:block" />
                )}
              </div>
            ))}
          </div>
        </Tarjeta>
      )}
      <BarraAccion>
        <Boton variante="texto" onClick={() => navegar({ to: '/compras/recepciones' })}>
          Cancelar
        </Boton>
        <Boton cargando={guardando} disabled={!orden} icono="move_to_inbox" onClick={() => orden && guardar(orden)}>
          Registrar recepción
        </Boton>
      </BarraAccion>
    </div>
  );
}

/** Compra directa sin orden [SUP-04]: requiere `compras.compra_directa`. */
function CompraDirecta() {
  const navegar = useNavigate();
  const registrar = useRegistrarRecepcion();
  const { data: almacenes } = useListarAlmacenes({ activo: true });
  const [proveedorId, setProveedorId] = useState('');
  const [almacenId, setAlmacenId] = useState('');
  const [factura, setFactura] = useState('');
  const [notas, setNotas] = useState('');
  const [lineas, setLineas] = useState<LineaDirecta[]>([nuevaLinea()]);
  const [guardando, setGuardando] = useState(false);
  const { errores, setErrores, general, setGeneral, aplicar } = useErrores();

  useEffect(() => {
    if (!almacenId && almacenes) {
      const mp = almacenes.find((a) => a.codigo === 'MP');
      if (mp) setAlmacenId(mp.id);
    }
  }, [almacenes, almacenId]);

  const cambiar = (clave: string, c: Partial<LineaDirecta>) => setLineas((ls) => ls.map((l) => (l.clave === clave ? { ...l, ...c } : l)));
  const capturadas = lineas.filter((l) => l.producto);
  const totales = totalesOrden(capturadas.map((l) => ({ cantidad: l.cantidad, precioUnitario: l.precioUnitario, tasaIva: l.producto!.tasaIva })));

  const guardar = async () => {
    setGeneral(null);
    const e: Record<string, string> = {};
    if (!proveedorId) e.proveedorId = 'Elige el proveedor.';
    if (!almacenId) e.almacenId = 'Elige el almacén.';
    if (capturadas.length === 0) e.partidas = 'Agrega al menos un producto.';
    capturadas.forEach((l) => {
      if (!(Number(l.cantidad) > 0)) e[`${l.clave}.cantidad`] = 'Mayor que cero.';
      if (l.precioUnitario === '') e[`${l.clave}.precio`] = 'Captura el precio.';
      if (l.producto!.manejaLote && !l.lote.trim()) e[`${l.clave}.lote`] = 'Captura el lote.';
    });
    setErrores(e);
    if (Object.keys(e).length) return;
    setGuardando(true);
    try {
      const recepcion = await registrar.mutateAsync({
        data: {
          proveedorId,
          almacenId,
          facturaProveedor: factura || null,
          notas: notas || null,
          partidas: capturadas.map((l) => ({
            productoId: l.producto!.id,
            unidadId: l.unidadId,
            cantidad: Number(l.cantidad),
            precioUnitario: Number(l.precioUnitario),
            lote: l.producto!.manejaLote ? l.lote.trim() : null,
            fechaCaducidad: l.producto!.manejaLote && l.caducidad ? l.caducidad : null,
          })),
        },
      });
      toast.success(`Recepción ${recepcion.folio} registrada.`);
      await navegar({ to: '/compras/recepciones/$id', params: { id: recepcion.id } });
    } catch (err) {
      aplicar(err);
    } finally {
      setGuardando(false);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      {general && <Aviso tipo="error">{general}</Aviso>}
      <Aviso tipo="advertencia">Compra sin orden: queda registrada a tu nombre como compra directa.</Aviso>
      <Tarjeta>
        <div className="grid gap-4 md:grid-cols-2">
          <SelectorProveedor value={proveedorId} onChange={setProveedorId} error={errores.proveedorId} />
          <SelectorAlmacen etiqueta="Almacén" vacio="Elige el almacén" value={almacenId} onChange={setAlmacenId} error={errores.almacenId} />
          <Campo etiqueta="Factura del proveedor (opcional)" value={factura} onChange={(e) => setFactura(e.target.value)} />
          <Campo etiqueta="Notas" value={notas} onChange={(e) => setNotas(e.target.value)} />
        </div>
      </Tarjeta>
      <Tarjeta
        titulo="Productos"
        acciones={
          <Boton variante="secundario" tamano="chico" icono="add" onClick={() => setLineas((ls) => [...ls, nuevaLinea()])}>
            Agregar producto
          </Boton>
        }
      >
        {errores.partidas && <p className="m-0 mb-2 text-sm text-error">{errores.partidas}</p>}
        <div className="flex flex-col gap-3">
          {lineas.map((l, i) => (
            <div key={l.clave} className="grid items-start gap-3 rounded-xl border border-row-divider p-3 lg:grid-cols-4">
              <SelectorProducto
                etiqueta={`Producto ${i + 1}`}
                className="lg:col-span-2"
                valor={l.producto?.id}
                producto={l.producto}
                filtro={{ esComprable: true }}
                onCambiar={(p) =>
                  cambiar(l.clave, { producto: p, unidadId: p ? (p.conversiones.find((c) => c.usoCompra)?.unidadId ?? p.unidadBaseId) : '' })
                }
              />
              <Selector
                etiqueta="Unidad"
                vacio="Elige"
                opciones={l.producto ? unidadesCompra(l.producto) : []}
                value={l.unidadId}
                disabled={!l.producto}
                onChange={(e) => cambiar(l.clave, { unidadId: e.target.value })}
              />
              <div className="flex items-start gap-2">
                <Campo
                  etiqueta="Cantidad"
                  type="number"
                  step="any"
                  inputMode="decimal"
                  className="flex-1"
                  value={l.cantidad}
                  onChange={(e) => cambiar(l.clave, { cantidad: e.target.value })}
                  error={errores[`${l.clave}.cantidad`]}
                />
                <BotonIcono
                  icono="delete"
                  etiqueta="Quitar producto"
                  className="mt-2"
                  disabled={lineas.length === 1}
                  onClick={() => setLineas((ls) => ls.filter((x) => x.clave !== l.clave))}
                />
              </div>
              <Campo
                etiqueta="Precio sin IVA"
                type="number"
                step="any"
                inputMode="decimal"
                prefijo="$"
                value={l.precioUnitario}
                onChange={(e) => cambiar(l.clave, { precioUnitario: e.target.value })}
                error={errores[`${l.clave}.precio`]}
              />
              {l.producto?.manejaLote && (
                <CamposLote
                  lote={l.lote}
                  caducidad={l.caducidad || (l.producto.vidaUtilDias ? sumarDias(fechaOperativa(), l.producto.vidaUtilDias) : '')}
                  onLote={(v) => cambiar(l.clave, { lote: v })}
                  onCaducidad={(v) => cambiar(l.clave, { caducidad: v })}
                  error={errores[`${l.clave}.lote`]}
                />
              )}
            </div>
          ))}
        </div>
      </Tarjeta>
      <BarraAccion
        resumen={
          <span className="flex flex-wrap gap-x-4">
            <span>Subtotal {formatoCentavos(totales.subtotal)}</span>
            <span>IVA {formatoCentavos(totales.iva)}</span>
            <strong>Total {formatoCentavos(totales.total)}</strong>
          </span>
        }
      >
        <Boton variante="texto" onClick={() => navegar({ to: '/compras/recepciones' })}>
          Cancelar
        </Boton>
        <Boton cargando={guardando} icono="move_to_inbox" onClick={guardar}>
          Registrar compra directa
        </Boton>
      </BarraAccion>
    </div>
  );
}

export function RecepcionNueva() {
  const { oc } = useSearch({ strict: false }) as { oc?: string };
  const { tienePermiso } = useSesion();
  const [modo, setModo] = useState('orden');
  const directa = tienePermiso('compras.compra_directa');
  return (
    <Pagina>
      <EncabezadoPagina
        titulo="Nueva recepción"
        subtitulo="Registra lo que entregó el proveedor; cada partida entra al almacén con su costo sin IVA."
        migas={[{ etiqueta: 'Compras' }, { etiqueta: 'Recepciones', a: '/compras/recepciones' }, { etiqueta: 'Nueva' }]}
      />
      {directa && !oc ? (
        <Pestanas
          valor={modo}
          onCambiar={setModo}
          pestanas={[
            { valor: 'orden', etiqueta: 'Contra orden de compra', contenido: <ContraOrden /> },
            { valor: 'directa', etiqueta: 'Compra directa', contenido: <CompraDirecta /> },
          ]}
        />
      ) : (
        <ContraOrden ordenInicial={oc} />
      )}
    </Pagina>
  );
}
