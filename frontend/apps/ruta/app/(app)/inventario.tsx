import { formatoCantidad } from '@tg/shared';
import { View } from 'react-native';
import { inventario } from '@/db/consultas';
import { useSync } from '@/servicios/sincronizacion';
import { Pantalla, Tarjeta, Texto, Vacio, estilos } from '@/ui/componentes';
import { useColores } from '@/ui/tema';
import { useDatos } from '@/ui/useDatos';

/** Inventario de camioneta: cargado, vendido, devuelto, cambios y existencia actual por producto, en cajas + paquetes (06). */
export default function Inventario() {
  const c = useColores();
  const { sincronizar, sincronizando } = useSync();
  const lineas = useDatos(inventario);
  return (
    <Pantalla alRefrescar={sincronizar} refrescando={sincronizando}>
      {lineas.length === 0 && <Vacio icono="local-shipping" texto="Sin carga recibida. Sincroniza cuando despachen tu carga." />}
      {lineas.map((l) => (
        <Tarjeta key={l.productoId}>
          <View style={estilos.filaEntre}>
            <View style={{ flex: 1 }}>
              <Texto negrita>{l.nombre}</Texto>
              <Texto suave>{l.sku}</Texto>
            </View>
            <Texto negrita tamano={22} estilo={{ color: l.existencia < 0 ? c.estado.rojo[1] : c.texto }}>
              {formatoCantidad(l.existencia, 3)} {l.unidad}
            </Texto>
          </View>
          {l.cajas && <Texto suave>= {l.cajas}</Texto>}
          <View style={estilos.filaEntre}>
            <Texto suave>Cargado {formatoCantidad(l.cargado, 3)}</Texto>
            <Texto suave>Vendido {formatoCantidad(l.vendido, 3)}</Texto>
            <Texto suave>Devuelto {formatoCantidad(l.devuelto, 3)}</Texto>
            <Texto suave>Cambios {formatoCantidad(l.cambios, 3)}</Texto>
          </View>
        </Tarjeta>
      ))}
    </Pantalla>
  );
}
