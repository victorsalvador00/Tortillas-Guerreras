import { formatoFechaHora } from '@tg/shared';
import { router } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { operaciones } from '@/db/consultas';
import { useSync } from '@/servicios/sincronizacion';
import { Boton, Etiqueta, Opciones, Pantalla, Tarjeta, Texto, Vacio, estilos } from '@/ui/componentes';
import { useDatos } from '@/ui/useDatos';

/** Pendientes: operaciones por enviar y rechazadas con su motivo (una operación nunca se pierde, 05). */
export default function Pendientes() {
  const { sincronizar, sincronizando } = useSync();
  const [filtro, setFiltro] = useState<'pendientes' | 'rechazadas' | 'todas'>('pendientes');
  const lista = useDatos(operaciones);
  const visibles = lista.filter((o) =>
    filtro === 'todas' ? true : filtro === 'rechazadas' ? o.estado === 'Rechazada' : o.estado === 'Pendiente' || o.estado === 'Enviando',
  );
  return (
    <Pantalla alRefrescar={sincronizar} refrescando={sincronizando}>
      <Opciones
        opciones={[
          { valor: 'pendientes', texto: `Por enviar (${lista.filter((o) => o.estado === 'Pendiente' || o.estado === 'Enviando').length})` },
          { valor: 'rechazadas', texto: `Rechazadas (${lista.filter((o) => o.estado === 'Rechazada').length})` },
          { valor: 'todas', texto: 'Todas' },
        ]}
        valor={filtro}
        alCambiar={setFiltro}
      />
      <Boton texto="Sincronizar ahora" icono="sync" cargando={sincronizando} alPresionar={() => void sincronizar()} />
      {visibles.length === 0 && <Vacio icono="task-alt" texto="No hay operaciones en esta lista." />}
      {visibles.map((o) => (
        <Tarjeta
          key={o.opId}
          alPresionar={o.documentoId && (o.tipo === 'RegistrarVenta' || o.tipo === 'EntregarPedido') ? () => router.push(`/ticket/${o.documentoId}`) : undefined}
        >
          <View style={estilos.filaEntre}>
            <Texto negrita>{o.descripcion ?? o.tipo}</Texto>
            <Etiqueta estado={o.estado} />
          </View>
          <Texto suave>
            #{o.secuencia} · {formatoFechaHora(o.creadaEn)}
            {o.intentos > 0 ? ` · ${o.intentos} intento(s)` : ''}
          </Texto>
          {o.error && <Texto estilo={{ color: o.estado === 'Rechazada' ? '#b71c1c' : '#7a5a00' }}>{o.error}</Texto>}
        </Tarjeta>
      ))}
    </Pantalla>
  );
}
