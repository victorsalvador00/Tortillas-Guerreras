import { fechaOperativa, formatoCentavos } from '@tg/shared';
import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { Linking, View } from 'react-native';
import { clientesRuta } from '@/db/consultas';
import { enlacesComoLlegar } from '@/dominio/ventas';
import { useSync } from '@/servicios/sincronizacion';
import { Boton, Campo, Etiqueta, Opciones, Pantalla, Tarjeta, Texto, Vacio, estilos } from '@/ui/componentes';
import { useDatos } from '@/ui/useDatos';

/** Clientes de hoy en orden de visita según `dias_visita`, con visitado/no visitado y "Cómo llegar". */
export default function Clientes() {
  const hoy = fechaOperativa();
  const { sincronizar, sincronizando } = useSync();
  const [alcance, setAlcance] = useState<'hoy' | 'todos'>('hoy');
  const [buscar, setBuscar] = useState('');
  const clientes = useDatos(() => ({ hoy: clientesRuta(hoy, true), todos: clientesRuta(hoy, false) }));
  const lista = useMemo(() => {
    const base = alcance === 'hoy' ? clientes.hoy : clientes.todos;
    const t = buscar.trim().toLowerCase();
    return t ? base.filter((c) => `${c.nombre} ${c.codigo} ${c.direccion ?? ''}`.toLowerCase().includes(t)) : base;
  }, [clientes, alcance, buscar]);
  const visitados = clientes.hoy.filter((c) => c.visitado && !c.esGenerico).length;
  const programados = clientes.hoy.filter((c) => !c.esGenerico).length;

  return (
    <Pantalla alRefrescar={sincronizar} refrescando={sincronizando}>
      <Texto suave>
        {visitados} de {programados} clientes visitados hoy
      </Texto>
      <Opciones
        opciones={[
          { valor: 'hoy', texto: 'Hoy' },
          { valor: 'todos', texto: 'Toda la ruta' },
        ]}
        valor={alcance}
        alCambiar={setAlcance}
      />
      <Campo etiqueta="Buscar cliente" value={buscar} onChangeText={setBuscar} placeholder="Nombre, código o dirección" />
      {lista.length === 0 && <Vacio icono="groups" texto="No hay clientes. Sincroniza para descargar tu ruta." />}
      {lista.map((c) => {
        const enlaces = c.latitud != null && c.longitud != null ? enlacesComoLlegar(c.latitud, c.longitud) : null;
        return (
          <Tarjeta key={c.id} alPresionar={() => router.push(`/clientes/${c.id}`)}>
            <View style={estilos.filaEntre}>
              <View style={{ flex: 1 }}>
                <Texto negrita tamano={16}>
                  {c.esGenerico ? c.nombre : `${c.ordenVisita}. ${c.nombre}`}
                </Texto>
                {c.direccion && <Texto suave>{c.direccion}</Texto>}
                {c.condicionPago === 'Credito' && <Texto suave>Saldo {formatoCentavos(Math.round(c.saldo * 100))}</Texto>}
              </View>
              {!c.esGenerico && <Etiqueta estado={c.visitado ? 'Visitado' : 'NoVisitado'} />}
            </View>
            {enlaces && (
              <View style={estilos.fila}>
                <View style={{ flex: 1 }}>
                  <Boton texto="Google Maps" icono="directions" variante="secundario" alPresionar={() => void Linking.openURL(enlaces.googleMaps)} />
                </View>
                <View style={{ flex: 1 }}>
                  <Boton texto="Waze" icono="navigation" variante="secundario" alPresionar={() => void Linking.openURL(enlaces.waze)} />
                </View>
              </View>
            )}
          </Tarjeta>
        );
      })}
    </Pantalla>
  );
}
