import { useNavigate, useParams } from '@tanstack/react-router';
import {
  esErrorApi,
  mensajeError,
  obtenerProducto,
  useActualizarOrdenCompra,
  useCrearOrdenCompra,
  useListarAlmacenes,
  useListarPreciosProveedor,
  useListarProductosProveedor,
  useObtenerOrdenCompra,
  type OrdenCompraDto,
  type ProductoDto,
} from '@tg/api-client';
import { formatoCentavos, formatoPorcentaje } from '@tg/shared';
import { Aviso, BarraAccion, Boton, BotonIcono, Campo, EstadoVacio, Selector, Tarjeta, toast } from '@tg/ui';
import { useEffect, useMemo, useState } from 'react';
import { SelectorAlmacen, SelectorProducto } from '@/componentes/comunes';
import { EncabezadoPagina, EstadoConsulta, Pagina } from '@/app/Pagina';
import { precioSugerido, totalesOrden, unidadesCompra } from './calculos';
import { SelectorProveedor } from './comunes';

interface Partida {
  clave: string;
  producto: ProductoDto | null;
  unidadId: string;
  cantidad: string;
  precioUnitario: string;
}

const nuevaPartida = (): Partida => ({ clave: crypto.randomUUID(), producto: null, unidadId: '', cantidad: '', precioUnitario: '' });

export function OrdenCompraFormulario() {
  const { id } = useParams({ strict: false }) as { id?: string };
  const consulta = useObtenerOrdenCompra(id ?? '', { query: { enabled: !!id } });
  const [productos, setProductos] = useState<Record<string, ProductoDto> | null>(id ? null : {});

  // Al editar se cargan los productos de las partidas para conocer sus unidades de compra.
  useEffect(() => {
    if (!consulta.data) return;
    const ids = [...new Set(consulta.data.partidas.map((p) => p.productoId))];
    void Promise.all(ids.map((pid) => obtenerProducto(pid))).then((lista) =>
      setProductos(Object.fromEntries(lista.map((p) => [p.id, p]))),
    );
  }, [consulta.data]);

  return (
    <Pagina>
      <EstadoConsulta cargando={!!id && (consulta.isLoading || productos === null)} error={consulta.error}>
        {(!id || (consulta.data && productos)) && <Formulario orden={consulta.data} productos={productos ?? {}} />}
      </EstadoConsulta>
    </Pagina>
  );
}

function Formulario({ orden, productos }: { orden?: OrdenCompraDto; productos: Record<string, ProductoDto> }) {
  const navegar = useNavigate();
  const crear = useCrearOrdenCompra();
  const actualizar = useActualizarOrdenCompra();
  const { data: almacenes } = useListarAlmacenes({ activo: true });
  const [proveedorId, setProveedorId] = useState(orden?.proveedorId ?? '');
  const [almacenId, setAlmacenId] = useState(orden?.almacenDestinoId ?? '');
  const [fechaEntrega, setFechaEntrega] = useState(orden?.fechaEntregaEsperada ?? '');
  const [notas, setNotas] = useState(orden?.notas ?? '');
  const [partidas, setPartidas] = useState<Partida[]>(
    orden?.partidas.map((p) => ({
      clave: p.id,
      producto: productos[p.productoId] ?? null,
      unidadId: p.unidadId,
      cantidad: String(p.cantidad),
      precioUnitario: String(p.precioUnitario),
    })) ?? [nuevaPartida()],
  );
  const [errores, setErrores] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const precios = useListarPreciosProveedor(proveedorId, undefined, { query: { enabled: !!proveedorId } });
  // RN-COM-12: solo se piden productos del catálogo activo del proveedor.
  const catalogo = useListarProductosProveedor(proveedorId, undefined, { query: { enabled: !!proveedorId } });
  const permitidos = useMemo(() => new Set((catalogo.data ?? []).map((c) => c.productoId)), [catalogo.data]);

  // Por defecto las compras entran al almacén de materias primas.
  useEffect(() => {
    if (!almacenId && almacenes) {
      const mp = almacenes.find((a) => a.codigo === 'MP') ?? almacenes.find((a) => a.tipo !== 'Ruta');
      if (mp) setAlmacenId(mp.id);
    }
  }, [almacenes, almacenId]);

  const cambiar = (clave: string, cambios: Partial<Partida>) =>
    setPartidas((ps) => ps.map((p) => (p.clave === clave ? { ...p, ...cambios } : p)));

  const elegirProducto = (clave: string, producto: ProductoDto | null) => {
    if (!producto) {
      cambiar(clave, { producto: null, unidadId: '', precioUnitario: '' });
      return;
    }
    // Sugiere el precio pactado con el proveedor; si no hay, el último pagado (RN-COM-12).
    const sugerido = precioSugerido(
      producto,
      catalogo.data?.find((x) => x.productoId === producto.id),
      precios.data?.find((x) => x.productoId === producto.id),
    );
    cambiar(clave, { producto, unidadId: sugerido.unidadId, precioUnitario: sugerido.precio });
  };

  const capturadas = partidas.filter((p) => p.producto);
  const totales = totalesOrden(
    capturadas.map((p) => ({ cantidad: p.cantidad, precioUnitario: p.precioUnitario, tasaIva: p.producto!.tasaIva })),
  );

  const validar = (): boolean => {
    const e: Record<string, string> = {};
    if (!proveedorId) e.proveedorId = 'Elige el proveedor.';
    if (!almacenId) e.almacenDestinoId = 'Elige el almacén destino.';
    if (capturadas.length === 0) e.partidas = 'Agrega al menos un producto.';
    capturadas.forEach((p) => {
      if (!(Number(p.cantidad) > 0)) e[`${p.clave}.cantidad`] = 'Mayor que cero.';
      if (p.precioUnitario === '' || !(Number(p.precioUnitario) >= 0)) e[`${p.clave}.precio`] = 'Captura el precio.';
      if (!p.unidadId) e[`${p.clave}.unidad`] = 'Elige la unidad.';
    });
    setErrores(e);
    return Object.keys(e).length === 0;
  };

  const guardar = async () => {
    setError(null);
    if (!validar()) return;
    setGuardando(true);
    try {
      const datos = {
        proveedorId,
        almacenDestinoId: almacenId,
        fechaEntregaEsperada: fechaEntrega || null,
        notas: notas || null,
        partidas: capturadas.map((p) => ({
          productoId: p.producto!.id,
          unidadId: p.unidadId,
          cantidad: Number(p.cantidad),
          precioUnitario: Number(p.precioUnitario),
        })),
      };
      const guardada = orden ? await actualizar.mutateAsync({ id: orden.id, data: datos }) : await crear.mutateAsync({ data: datos });
      toast.success(orden ? 'Orden actualizada.' : `Orden ${guardada.folio} creada en borrador.`);
      await navegar({ to: '/compras/ordenes/$id', params: { id: guardada.id } });
    } catch (e) {
      if (esErrorApi(e)) setErrores(Object.fromEntries(Object.entries(e.errores).map(([c, m]) => [c, m[0]])));
      setError(mensajeError(e));
    } finally {
      setGuardando(false);
    }
  };

  if (orden && orden.estado !== 'Borrador') {
    return <EstadoVacio icono="lock" titulo="No se puede editar" mensaje="Una orden solo se edita en Borrador (RN-COM-02)." />;
  }

  return (
    <>
      <EncabezadoPagina
        titulo={orden ? `Editar ${orden.folio}` : 'Nueva orden de compra'}
        subtitulo="Cantidades y precios en la unidad de compra de cada producto; precios sin IVA."
        migas={[{ etiqueta: 'Compras' }, { etiqueta: 'Órdenes de compra', a: '/compras/ordenes' }, { etiqueta: orden ? orden.folio : 'Nueva' }]}
      />
      <div className="flex flex-col gap-4">
        {error && <Aviso tipo="error">{error}</Aviso>}
        <Tarjeta>
          <div className="grid gap-4 md:grid-cols-3">
            <SelectorProveedor value={proveedorId} onChange={setProveedorId} error={errores.proveedorId} />
            <SelectorAlmacen etiqueta="Entrega en" vacio="Elige el almacén" value={almacenId} onChange={setAlmacenId} error={errores.almacenDestinoId} />
            <Campo etiqueta="Fecha esperada" type="date" value={fechaEntrega} onChange={(e) => setFechaEntrega(e.target.value)} ayuda="Opcional." />
            <Campo etiqueta="Notas" className="md:col-span-3" value={notas} onChange={(e) => setNotas(e.target.value)} />
          </div>
        </Tarjeta>

        <Tarjeta
          titulo="Productos"
          subtitulo="Solo productos que se compran (RN-COM-01)."
          acciones={
            <Boton variante="secundario" tamano="chico" icono="add" onClick={() => setPartidas((ps) => [...ps, nuevaPartida()])}>
              Agregar producto
            </Boton>
          }
        >
          {errores.partidas && <p className="m-0 mb-2 text-sm text-error">{errores.partidas}</p>}
          {proveedorId && catalogo.data && catalogo.data.length === 0 && (
            <Aviso tipo="advertencia" className="mb-3">
              Este proveedor no tiene productos en su catálogo. Agrégalos en Compras → Proveedores → Productos que vende.
            </Aviso>
          )}
          <div className="flex flex-col gap-3">
            {partidas.map((p, i) => {
              const importe = p.producto
                ? totalesOrden([{ cantidad: p.cantidad, precioUnitario: p.precioUnitario, tasaIva: p.producto.tasaIva }])
                : null;
              return (
                <div key={p.clave} className="grid items-start gap-3 rounded-xl border border-row-divider p-3 lg:grid-cols-[2fr_1fr_162px_189px_auto]">
                  <SelectorProducto
                    etiqueta={`Producto ${i + 1}`}
                    valor={p.producto?.id}
                    producto={p.producto}
                    filtro={{ esComprable: true }}
                    permitidos={permitidos}
                    deshabilitado={!proveedorId}
                    onCambiar={(prod) => elegirProducto(p.clave, prod)}
                  />
                  <Selector
                    etiqueta="Unidad"
                    vacio="Elige"
                    opciones={p.producto ? unidadesCompra(p.producto) : []}
                    value={p.unidadId}
                    onChange={(e) => cambiar(p.clave, { unidadId: e.target.value })}
                    error={errores[`${p.clave}.unidad`]}
                    disabled={!p.producto}
                  />
                  <Campo
                    etiqueta="Cantidad"
                    type="number"
                    step="any"
                    inputMode="decimal"
                    value={p.cantidad}
                    onChange={(e) => cambiar(p.clave, { cantidad: e.target.value })}
                    error={errores[`${p.clave}.cantidad`] ?? errores[`partidas[${i}].cantidad`]}
                  />
                  <Campo
                    etiqueta="Precio sin IVA"
                    type="number"
                    step="any"
                    inputMode="decimal"
                    prefijo="$"
                    value={p.precioUnitario}
                    onChange={(e) => cambiar(p.clave, { precioUnitario: e.target.value })}
                    error={errores[`${p.clave}.precio`]}
                    ayuda={importe ? `${formatoCentavos(importe.subtotal)} · IVA ${formatoPorcentaje(p.producto!.tasaIva)}` : undefined}
                  />
                  <BotonIcono
                    icono="delete"
                    etiqueta="Quitar producto"
                    className="mt-2"
                    disabled={partidas.length === 1}
                    onClick={() => setPartidas((ps) => ps.filter((x) => x.clave !== p.clave))}
                  />
                </div>
              );
            })}
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
          <Boton variante="texto" onClick={() => navegar({ to: orden ? '/compras/ordenes/$id' : '/compras/ordenes', params: { id: orden?.id ?? '' } })}>
            Cancelar
          </Boton>
          <Boton cargando={guardando} onClick={guardar}>
            {orden ? 'Guardar cambios' : 'Guardar borrador'}
          </Boton>
        </BarraAccion>
      </div>
    </>
  );
}
