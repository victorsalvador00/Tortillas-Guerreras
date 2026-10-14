import { formatoCantidad, formatoCentavos } from '@tg/shared';
import { router, useLocalSearchParams } from 'expo-router';
import { useMemo, useState } from 'react';
import { View } from 'react-native';
import { cliente, pedido } from '@/db/consultas';
import { confirmarEntrega } from '@/db/operaciones';
import { calcularVenta } from '@/dominio/ventas';
import { leerGps } from '@/servicios/gps';
import { useSync } from '@/servicios/sincronizacion';
import { Aviso, Boton, Cantidad, Opciones, Pantalla, Tarjeta, Texto, Vacio, estilos } from '@/ui/componentes';
import { useDatos } from '@/ui/useDatos';

type FormaPago = 'Efectivo' | 'Transferencia' | 'Credito';

/** Entregar pedido: partidas con cantidades a entregar (por defecto lo pendiente) y confirmar tomando GPS (RN-VEN-08). */
export default function Entrega() {
  const { pedidoId } = useLocalSearchParams<{ pedidoId: string }>();
  const { notificarCambio, sincronizar } = useSync();
  const datos = useDatos(() => {
    const p = pedido(pedidoId);
    return { pedido: p, cliente: p ? cliente(p.clienteId) : null };
  });
  const p = datos.pedido;
  const c = datos.cliente;
  const [cantidades, setCantidades] = useState<Record<string, number>>(() =>
    Object.fromEntries((p?.partidas ?? []).map((x) => [x.id, Math.round(x.pendiente * 10000) / 10000])),
  );
  const credito = c?.condicionPago === 'Credito';
  const [forma, setForma] = useState<FormaPago>(credito ? 'Credito' : 'Efectivo');
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const partidas = useMemo(
    () =>
      (p?.partidas ?? []).map((x) => ({
        pedidoPartidaId: x.id, productoId: x.productoId, unidadId: x.unidadId, unidadClave: x.unidadClave, descripcion: x.nombre,
        cantidad: cantidades[x.id] ?? 0, factorABase: x.factorABase, precioUnitario: x.precioUnitario, tasaIva: x.tasaIva, tasaIeps: x.tasaIeps,
      })),
    [p, cantidades],
  );
  const total = calcularVenta(partidas).total;

  if (!p || !c) return <Vacio texto="El pedido ya no está asignado a tu ruta." />;

  const confirmar = async () => {
    if (partidas.every((x) => x.cantidad <= 0)) {
      setError('Captura al menos una cantidad entregada; si el cliente no lo recibe, registra un rechazo en Retorno.');
      return;
    }
    if (partidas.some((x) => x.cantidad > (p.partidas.find((y) => y.id === x.pedidoPartidaId)?.pendiente ?? 0) + 1e-9)) {
      setError('No puedes entregar más de lo pendiente del pedido.');
      return;
    }
    setGuardando(true);
    try {
      const gps = await leerGps();
      const r = confirmarEntrega({
        pedidoId: p.id, clienteId: c.id, formaPago: forma, condicionPago: credito ? 'Credito' : 'Contado', partidas, gps,
      });
      notificarCambio();
      void sincronizar();
      router.replace(`/ticket/${r.id}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo guardar la entrega.');
      setGuardando(false);
    }
  };

  return (
    <Pantalla
      pie={
        <>
          <View style={estilos.filaEntre}>
            <Texto suave>Total a entregar</Texto>
            <Texto negrita tamano={22}>
              {formatoCentavos(total)}
            </Texto>
          </View>
          {error && <Aviso tipo="rojo">{error}</Aviso>}
          <Boton texto="Confirmar entrega" icono="check-circle" grande cargando={guardando} alPresionar={confirmar} />
        </>
      }
    >
      <Tarjeta>
        <Texto negrita tamano={17}>
          {p.folio} · {c.nombre}
        </Texto>
        <Texto suave>Entrega programada {p.fechaEntrega}</Texto>
        {!credito && (
          <Opciones<FormaPago>
            opciones={[
              { valor: 'Efectivo', texto: 'Efectivo' },
              { valor: 'Transferencia', texto: 'Transferencia' },
            ]}
            valor={forma}
            alCambiar={setForma}
          />
        )}
        {credito && <Texto suave>Cliente a crédito: la entrega se registra a crédito.</Texto>}
      </Tarjeta>
      {p.partidas.map((x) => (
        <Tarjeta key={x.id}>
          <Texto negrita>{x.nombre}</Texto>
          <Texto suave>
            Pedido {formatoCantidad(x.cantidad)} {x.unidadClave} · pendiente {formatoCantidad(x.pendiente)}
          </Texto>
          <Cantidad
            valor={cantidades[x.id] ?? 0}
            decimales={3}
            alCambiar={(v) => setCantidades((s) => ({ ...s, [x.id]: v }))}
          />
        </Tarjeta>
      ))}
    </Pantalla>
  );
}
