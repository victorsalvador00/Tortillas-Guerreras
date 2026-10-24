import { formatoCantidad } from '@tg/shared';
import { useState } from 'react';
import { View } from 'react-native';
import { alertasCantidad, textoAlerta, type AlertaCantidad } from '@/dominio/pedidos';
import { Aviso, Tarjeta, Texto, estilos } from './componentes';

interface PartidaRevisable {
  productoId: string;
  descripcion: string;
  cantidad: number;
  unidadClave: string;
  factorABase: number;
}

/**
 * RN-APP-09: calcula las alertas de cantidad y exige un segundo toque para confirmar. `revisar()` devuelve `true` si ya
 * se puede guardar; cualquier cambio de cantidades debe llamar `reiniciar()`.
 */
export function useCantidadesConfiables(
  partidas: readonly PartidaRevisable[],
  referencia: { promedios: ReadonlyMap<string, number>; factor: number; maxima: number },
) {
  const [confirmado, setConfirmado] = useState(false);
  const [mostrar, setMostrar] = useState(false);
  const alertas = alertasCantidad(partidas, referencia.promedios, referencia.factor, referencia.maxima);
  return {
    alertas,
    pendiente: mostrar && alertas.length > 0 && !confirmado,
    revisar(): boolean {
      if (alertas.length === 0 || confirmado) return true;
      if (mostrar) {
        setConfirmado(true);
        return true;
      }
      setMostrar(true);
      return false;
    },
    reiniciar() {
      setConfirmado(false);
      setMostrar(false);
    },
  };
}

/** "¿Seguro? Capturaste 200 (normalmente 20)" por partida, con la instrucción de tocar de nuevo. */
export function AvisoCantidades({ alertas }: { alertas: AlertaCantidad[] }) {
  return (
    <Aviso tipo="naranja">
      {alertas.map(textoAlerta).join('\n')}
      {'\n'}Revisa y toca de nuevo para confirmar.
    </Aviso>
  );
}

/** RN-APP-09: resumen antes de enviar con las cantidades en grande, en su unidad y en cajas + paquetes. */
export function ResumenCantidades({
  partidas,
  titulo = 'Revisa las cantidades',
}: {
  partidas: { clave: string; descripcion: string; cantidad: number; unidadClave: string; cajas: string | null; tipo?: string }[];
  titulo?: string;
}) {
  return (
    <Tarjeta titulo={titulo}>
      {partidas.map((p) => (
        <View key={p.clave} style={estilos.filaEntre}>
          <View style={{ flex: 1 }}>
            <Texto negrita>{p.descripcion}</Texto>
            {p.tipo === 'CambioFisico' && <Texto suave>Cambio</Texto>}
            {p.cajas && <Texto suave>= {p.cajas}</Texto>}
          </View>
          <Texto negrita tamano={28}>
            {formatoCantidad(p.cantidad)} <Texto suave>{p.unidadClave}</Texto>
          </Texto>
        </View>
      ))}
    </Tarjeta>
  );
}
