import { desglosarCajas, fechaOperativa, formatoCajas, formatoFecha } from '@tg/shared';
import { Stack, router, useLocalSearchParams } from 'expo-router';
import { useMemo, useState } from 'react';
import { View } from 'react-native';
import { canalRuta, cliente, pedidoLocal, productosParaVenta, referenciaCantidades, type ProductoVenta } from '@/db/consultas';
import { enviarPedido, guardarBorrador, type PartidaPedidoApp } from '@/db/operaciones';
import { siguienteDiaVisita } from '@/dominio/pedidos';
import { leerGps } from '@/servicios/gps';
import { useSync } from '@/servicios/sincronizacion';
import { AvisoCantidades, ResumenCantidades, useCantidadesConfiables } from '@/ui/cantidades';
import { Aviso, Boton, Campo, Cantidad, Opciones, Pantalla, Tarjeta, Texto, Vacio, estilos } from '@/ui/componentes';
import { useDatos } from '@/ui/useDatos';

type Tipo = 'Venta' | 'CambioFisico';

interface Captura {
  cantidad: number;
  notas: string;
}

const llave = (productoId: string, tipo: Tipo) => `${productoId}|${tipo}`;

/** Unidad en que se pide: la predeterminada de venta (paquetes) o la primera con precio. */
function unidadPedido(p: ProductoVenta) {
  return p.unidades.find((u) => u.predeterminada) ?? p.unidades[0];
}

function cajas(p: ProductoVenta, cantidad: number): string | null {
  const d = desglosarCajas(cantidad * unidadPedido(p).factorABase, p.conversiones, p.unidadBase);
  return d && d.cajas > 0 ? formatoCajas(d) : null;
}

/**
 * Nuevo pedido (06, v0.2): fecha de entrega (por defecto el siguiente día de visita, mínimo hoy, RN-VEN-15), OC
 * opcional, productos permitidos (RN-VEN-14) con cantidad en paquetes y su equivalente en cajas, y sección de cambios
 * (partidas `CambioFisico`). Se guarda como borrador local (RN-APP-11); **Enviar** muestra el resumen con las
 * cantidades en grande (RN-APP-09) y encola `CapturarPedido`. Con `ruta=1` es el pedido global de la ruta (RN-VEN-16):
 * todos los productos del canal en una cuadrícula con observaciones.
 */
export default function NuevoPedido() {
  const { clienteId, id, ruta } = useLocalSearchParams<{ clienteId: string; id?: string; ruta?: string }>();
  const { notificarCambio, sincronizar } = useSync();
  const hoy = fechaOperativa();
  const datos = useDatos(() => {
    const c = cliente(clienteId);
    return {
      cliente: c,
      productos: c ? productosParaVenta(c, hoy) : [],
      borrador: id ? pedidoLocal(id) : null,
      referencia: referenciaCantidades(clienteId),
      canal: canalRuta(),
    };
  });
  const c = datos.cliente;
  const b = datos.borrador;
  const esRuta = ruta === '1' || (b?.esRuta ?? false) || (!!c?.esGenerico && datos.canal?.pedidoPor === 'Ruta');
  const [fecha, setFecha] = useState(b?.fechaEntrega ?? siguienteDiaVisita(hoy, c?.diasVisita ?? []));
  const [oc, setOc] = useState(b?.ordenCompraCliente ?? '');
  const [notas, setNotas] = useState(b?.notas ?? '');
  const [capturas, setCapturas] = useState<Record<string, Captura>>(() =>
    Object.fromEntries((b?.partidas ?? []).map((x) => [llave(x.productoId, x.tipo), { cantidad: x.cantidad, notas: x.notas ?? '' }])),
  );
  const [seccion, setSeccion] = useState<Tipo>('Venta');
  const [buscar, setBuscar] = useState('');
  const [revisando, setRevisando] = useState(false);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [borradorId, setBorradorId] = useState<string | undefined>(b?.estado === 'Borrador' ? b.id : undefined);

  const partidas = useMemo<(PartidaPedidoApp & { cajas: string | null })[]>(
    () =>
      datos.productos.flatMap((p) =>
        (['Venta', 'CambioFisico'] as const).flatMap((tipo) => {
          const cap = capturas[llave(p.productoId, tipo)];
          if (!cap || cap.cantidad <= 0) return [];
          const u = unidadPedido(p);
          return [
            {
              productoId: p.productoId, unidadId: u.unidadId, unidadClave: u.clave, descripcion: p.nombreCorto, cantidad: cap.cantidad,
              factorABase: u.factorABase, tipo, notas: cap.notas.trim() || null, cajas: cajas(p, cap.cantidad),
            },
          ];
        }),
      ),
    [datos.productos, capturas],
  );
  const cantidadesConfiables = useCantidadesConfiables(partidas, datos.referencia);

  if (!c) return <Vacio texto="El cliente no está en tu ruta." />;
  if (b && b.estado !== 'Borrador') return <Vacio texto="Este pedido ya se envió y no se puede editar." />;

  const actualizar = (productoId: string, tipo: Tipo, cambio: Partial<Captura>) => {
    cantidadesConfiables.reiniciar();
    setRevisando(false);
    setCapturas((s) => {
      const actual = s[llave(productoId, tipo)] ?? { cantidad: 0, notas: '' };
      return { ...s, [llave(productoId, tipo)]: { ...actual, ...cambio } };
    });
  };

  const validar = (): string | null => {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(fecha)) return 'Captura la fecha de entrega como AAAA-MM-DD.';
    if (fecha < hoy) return 'La fecha de entrega no puede ser anterior a hoy.';
    if (partidas.length === 0) return 'Agrega al menos un producto.';
    return null;
  };

  const guardar = (): string => {
    const nuevoId = guardarBorrador({
      id: borradorId, clienteId: c.id, fechaEntrega: fecha, ordenCompraCliente: oc.trim() || null, notas: notas.trim() || null, esRuta,
      partidas,
    });
    setBorradorId(nuevoId);
    notificarCambio();
    return nuevoId;
  };

  const guardarYSalir = () => {
    setError(null);
    try {
      guardar();
      router.back();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo guardar el borrador.');
    }
  };

  const enviar = async () => {
    setError(null);
    const problema = validar();
    if (problema) {
      setError(problema);
      return;
    }
    if (!revisando) {
      setRevisando(true);
      return;
    }
    if (!cantidadesConfiables.revisar()) return;
    setGuardando(true);
    try {
      const pedidoId = guardar();
      enviarPedido(pedidoId, await leerGps());
      notificarCambio();
      void sincronizar();
      router.replace('/pedidos');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo enviar el pedido.');
      setGuardando(false);
    }
  };

  const visibles = datos.productos.filter((p) => {
    const t = buscar.trim().toLowerCase();
    return !t || `${p.nombre} ${p.nombreCorto} ${p.sku}`.toLowerCase().includes(t);
  });

  return (
    <Pantalla
      pie={
        <>
          {revisando && (
            <ResumenCantidades
              partidas={partidas.map((x) => ({
                clave: llave(x.productoId, x.tipo), descripcion: x.descripcion, cantidad: x.cantidad, unidadClave: x.unidadClave,
                cajas: x.cajas, tipo: x.tipo,
              }))}
              titulo={`Entrega ${formatoFecha(fecha)}`}
            />
          )}
          {cantidadesConfiables.pendiente && <AvisoCantidades alertas={cantidadesConfiables.alertas} />}
          {error && <Aviso tipo="rojo">{error}</Aviso>}
          <View style={estilos.fila}>
            <View style={{ flex: 1 }}>
              <Boton texto="Guardar borrador" icono="save" variante="secundario" deshabilitado={guardando} alPresionar={guardarYSalir} />
            </View>
            <View style={{ flex: 1 }}>
              <Boton
                texto={revisando ? (cantidadesConfiables.pendiente ? 'Enviar de todos modos' : 'Confirmar envío') : 'Enviar'}
                icono="send"
                cargando={guardando}
                deshabilitado={partidas.length === 0}
                alPresionar={enviar}
              />
            </View>
          </View>
        </>
      }
    >
      <Stack.Screen options={{ title: esRuta ? 'Pedido de la ruta' : 'Nuevo pedido' }} />
      <Tarjeta>
        <Texto negrita tamano={17}>
          {c.nombre}
        </Texto>
        <Campo
          etiqueta="Fecha de entrega"
          value={fecha}
          onChangeText={(v) => {
            setRevisando(false);
            setFecha(v.trim());
          }}
          placeholder="AAAA-MM-DD"
          ayuda={/^\d{4}-\d{2}-\d{2}$/.test(fecha) ? formatoFecha(fecha) : 'Mínimo hoy; puede ser cualquier fecha futura.'}
        />
        {!esRuta && <Campo etiqueta="Orden de compra del cliente (opcional)" value={oc} onChangeText={setOc} />}
        <Campo etiqueta="Notas (opcional)" value={notas} onChangeText={setNotas} />
      </Tarjeta>

      <Aviso tipo="azul">El borrador se queda en este teléfono hasta que lo envíes; si se reinstala la app, se pierde.</Aviso>

      {!esRuta && (
        <Opciones<Tipo>
          opciones={[
            { valor: 'Venta', texto: 'Productos' },
            { valor: 'CambioFisico', texto: 'Cambios' },
          ]}
          valor={seccion}
          alCambiar={setSeccion}
        />
      )}
      {seccion === 'CambioFisico' && <Texto suave>Producto que se cambiará al entregar: va a precio 0 y se entrega con Cambio físico.</Texto>}
      <Campo etiqueta="Buscar producto" value={buscar} onChangeText={setBuscar} />
      {visibles.length === 0 && <Vacio icono="inventory-2" texto="No hay productos con precio para este cliente. Sincroniza." />}
      {visibles.map((p) => {
        const tipo = esRuta ? 'Venta' : seccion;
        const cap = capturas[llave(p.productoId, tipo)];
        const u = unidadPedido(p);
        const equivalente = cap?.cantidad ? cajas(p, cap.cantidad) : null;
        return (
          <Tarjeta key={p.productoId}>
            <View style={estilos.filaEntre}>
              <Texto negrita>{p.nombreCorto}</Texto>
              <Texto suave>{u.clave}</Texto>
            </View>
            <Cantidad valor={cap?.cantidad ?? 0} decimales={u.decimales} alCambiar={(v) => actualizar(p.productoId, tipo, { cantidad: v })} />
            {equivalente && <Texto suave>= {equivalente}</Texto>}
            {esRuta && (cap?.cantidad ?? 0) > 0 && (
              <Campo etiqueta="Observaciones" value={cap?.notas ?? ''} onChangeText={(v) => actualizar(p.productoId, tipo, { notas: v })} />
            )}
          </Tarjeta>
        );
      })}
    </Pantalla>
  );
}
