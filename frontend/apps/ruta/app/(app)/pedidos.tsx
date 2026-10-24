import { fechaOperativa, formatoCantidad, formatoFecha, formatoFechaHora } from '@tg/shared';
import { router } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { cliente, pedidosLocales } from '@/db/consultas';
import { duplicarPedido, eliminarBorrador } from '@/db/operaciones';
import { siguienteDiaVisita } from '@/dominio/pedidos';
import { useSync } from '@/servicios/sincronizacion';
import { Aviso, Boton, Etiqueta, Opciones, Pantalla, Tarjeta, Texto, Vacio, estilos } from '@/ui/componentes';
import { useDatos } from '@/ui/useDatos';

type Filtro = 'Borrador' | 'Enviado' | 'Rechazado';

/**
 * Pedidos (06, v0.2): borradores, enviados (con el estado de su operación) y rechazados con su motivo; duplicar uno
 * anterior como borrador. Los borradores solo viven en este teléfono (RN-APP-11).
 */
export default function Pedidos() {
  const { notificarCambio, sincronizar, sincronizando } = useSync();
  const [filtro, setFiltro] = useState<Filtro>('Borrador');
  const [mensaje, setMensaje] = useState<string | null>(null);
  const lista = useDatos(pedidosLocales);
  const visibles = lista.filter((p) => p.estado === filtro);
  const cuenta = (e: Filtro) => lista.filter((p) => p.estado === e).length;

  const duplicar = (id: string, clienteId: string) => {
    const nuevo = duplicarPedido(id, siguienteDiaVisita(fechaOperativa(), cliente(clienteId)?.diasVisita ?? []));
    notificarCambio();
    router.push({ pathname: '/pedido/[clienteId]', params: { clienteId, id: nuevo } });
  };

  return (
    <Pantalla alRefrescar={sincronizar} refrescando={sincronizando}>
      <Aviso tipo="amarillo">
        Los borradores no se envían ni se respaldan: si se reinstala la app, se pierden. Lo enviado ya está en la cola y no se pierde.
      </Aviso>
      {mensaje && <Aviso tipo="verde">{mensaje}</Aviso>}
      <Opciones<Filtro>
        opciones={[
          { valor: 'Borrador', texto: `Borradores (${cuenta('Borrador')})` },
          { valor: 'Enviado', texto: `Enviados (${cuenta('Enviado')})` },
          { valor: 'Rechazado', texto: `Rechazados (${cuenta('Rechazado')})` },
        ]}
        valor={filtro}
        alCambiar={setFiltro}
      />
      {visibles.length === 0 && <Vacio icono="assignment" texto="No hay pedidos en esta lista." />}
      {visibles.map((p) => (
        <Tarjeta
          key={p.id}
          alPresionar={
            p.estado === 'Borrador' ? () => router.push({ pathname: '/pedido/[clienteId]', params: { clienteId: p.clienteId, id: p.id } }) : undefined
          }
        >
          <View style={estilos.filaEntre}>
            <View style={{ flex: 1 }}>
              <Texto negrita>{p.esRuta ? 'Pedido de la ruta' : p.cliente}</Texto>
              <Texto suave>
                Entrega {formatoFecha(p.fechaEntrega)} · {p.partidas.length} producto(s)
              </Texto>
            </View>
            {p.estado === 'Enviado' && p.estadoOperacion ? (
              <Etiqueta estado={p.estadoOperacion} />
            ) : (
              <Etiqueta texto={p.estado} color={p.estado === 'Rechazado' ? 'rojo' : 'gris'} />
            )}
          </View>
          <Texto suave>
            {p.partidas
              .slice(0, 4)
              .map((x) => `${x.descripcion} ${formatoCantidad(x.cantidad)}${x.tipo === 'CambioFisico' ? ' (cambio)' : ''}`)
              .join(' · ')}
            {p.partidas.length > 4 ? ' …' : ''}
          </Texto>
          {p.enviadoEn && <Texto suave>Enviado {formatoFechaHora(p.enviadoEn)}</Texto>}
          {p.error && <Texto estilo={{ color: '#b71c1c' }}>{p.error}</Texto>}
          <View style={estilos.fila}>
            <View style={{ flex: 1 }}>
              <Boton texto="Duplicar" icono="content-copy" variante="secundario" alPresionar={() => duplicar(p.id, p.clienteId)} />
            </View>
            {p.estado === 'Borrador' && (
              <View style={{ flex: 1 }}>
                <Boton
                  texto="Eliminar"
                  icono="delete"
                  variante="texto"
                  alPresionar={() => {
                    eliminarBorrador(p.id);
                    notificarCambio();
                    setMensaje('Borrador eliminado.');
                  }}
                />
              </View>
            )}
          </View>
        </Tarjeta>
      ))}
    </Pantalla>
  );
}
