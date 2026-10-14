import { aCentavos, formatoCentavos, formatoFecha, formatoFechaHora } from '@tg/shared';
import { Stack, router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Linking, View } from 'react-native';
import { cliente } from '@/db/consultas';
import { proponerUbicacion, registrarCobro } from '@/db/operaciones';
import { enlacesComoLlegar, textoCarga } from '@/dominio/ventas';
import { leerGps } from '@/servicios/gps';
import { useSync } from '@/servicios/sincronizacion';
import { Aviso, Boton, Campo, Etiqueta, Opciones, Pantalla, Tarjeta, Texto, Vacio, estilos } from '@/ui/componentes';
import { useDatos } from '@/ui/useDatos';

/** Cliente: datos, saldo [SUP-08], pedidos pendientes, historial y acciones (venta, entrega, retorno, cobro). */
export default function Cliente() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { notificarCambio } = useSync();
  const c = useDatos(() => cliente(id));
  const [cobrando, setCobrando] = useState(false);
  const [importe, setImporte] = useState('');
  const [forma, setForma] = useState<'Efectivo' | 'Transferencia'>('Efectivo');
  const [mensaje, setMensaje] = useState<string | null>(null);
  const [ubicando, setUbicando] = useState(false);

  if (!c) return <Vacio texto="El cliente no está en tu ruta." />;
  const enlaces = c.latitud != null && c.longitud != null ? enlacesComoLlegar(c.latitud, c.longitud) : null;

  const cobrar = () => {
    const n = Number(importe.replace(',', '.'));
    if (!Number.isFinite(n) || n <= 0) {
      setMensaje('Captura un importe mayor que cero.');
      return;
    }
    registrarCobro({ clienteId: c.id, importe: aCentavos(n), formaPago: forma });
    notificarCambio();
    setCobrando(false);
    setImporte('');
    setMensaje(`Cobro de ${formatoCentavos(aCentavos(n))} guardado; se enviará al sincronizar.`);
  };

  const ubicar = async () => {
    setUbicando(true);
    const gps = await leerGps();
    setUbicando(false);
    if (!gps) {
      setMensaje('No se obtuvo la ubicación. Revisa el GPS e intenta de nuevo.');
      return;
    }
    proponerUbicacion(c.id, gps);
    notificarCambio();
    setMensaje('Ubicación propuesta; un supervisor la aprobará.');
  };

  return (
    <Pantalla>
      <Stack.Screen options={{ title: c.nombre }} />
      {mensaje && <Aviso tipo="verde">{mensaje}</Aviso>}
      <Tarjeta>
        <Texto negrita tamano={18}>
          {c.nombre}
        </Texto>
        <Texto suave>
          {c.codigo} · {c.canalClave} · {c.condicionPago === 'Credito' ? `Crédito ${c.diasCredito} días` : 'Contado'}
        </Texto>
        {c.direccion && <Texto>{c.direccion}</Texto>}
        {c.telefono && (
          <Boton texto={c.telefono} icono="phone" variante="texto" alPresionar={() => void Linking.openURL(`tel:${c.telefono}`)} />
        )}
        {c.condicionPago === 'Credito' && (
          <View style={estilos.filaEntre}>
            <Texto>Saldo</Texto>
            <Texto negrita>{formatoCentavos(Math.round(c.saldo * 100))}</Texto>
          </View>
        )}
        {!c.ubicacionAprobada && !c.esGenerico && <Etiqueta texto="Ubicación por aprobar" color="amarillo" />}
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
        {!c.esGenerico && (
          <Boton texto="Proponer mi ubicación actual" icono="my-location" variante="texto" cargando={ubicando} alPresionar={ubicar} />
        )}
      </Tarjeta>

      <Boton texto="Nueva venta" icono="add-shopping-cart" grande alPresionar={() => router.push(`/venta/${c.id}`)} />
      <Boton texto="Retorno" icono="assignment-return" variante="secundario" alPresionar={() => router.push(`/retorno/${c.id}`)} />
      {c.condicionPago === 'Credito' && !cobrando && (
        <Boton texto="Registrar cobro" icono="request-quote" variante="secundario" alPresionar={() => setCobrando(true)} />
      )}
      {cobrando && (
        <Tarjeta titulo="Cobro">
          <Campo etiqueta="Importe" keyboardType="decimal-pad" value={importe} onChangeText={setImporte} placeholder="0.00" />
          <Opciones
            opciones={[
              { valor: 'Efectivo', texto: 'Efectivo' },
              { valor: 'Transferencia', texto: 'Transferencia' },
            ]}
            valor={forma}
            alCambiar={setForma}
          />
          <Boton texto="Guardar cobro" alPresionar={cobrar} />
          <Boton texto="Cancelar" variante="texto" alPresionar={() => setCobrando(false)} />
        </Tarjeta>
      )}

      <Tarjeta titulo="Pedidos por entregar">
        {c.pedidos.length === 0 && <Texto suave>Sin pedidos pendientes.</Texto>}
        {c.pedidos.map((p) => (
          <Tarjeta key={p.id} alPresionar={() => router.push(`/entrega/${p.id}`)}>
            <View style={estilos.filaEntre}>
              <Texto negrita>{p.folio}</Texto>
              <Etiqueta estado={p.estado} texto={p.estado} />
            </View>
            <Texto suave>
              Entrega {formatoFecha(p.fechaEntrega)} · {formatoCentavos(aCentavos(p.total))}
            </Texto>
            <Etiqueta texto={textoCarga(p.carga)} color={p.carga.enCamioneta ? 'verde' : 'amarillo'} />
          </Tarjeta>
        ))}
      </Tarjeta>

      <Tarjeta titulo="Últimas ventas en este teléfono">
        {c.ventas.length === 0 && <Texto suave>Sin ventas registradas.</Texto>}
        {c.ventas.map((v) => (
          <Tarjeta key={v.id} alPresionar={() => router.push(`/ticket/${v.id}`)}>
            <View style={estilos.filaEntre}>
              <Texto negrita>{v.folio}</Texto>
              <Texto negrita>{formatoCentavos(v.total)}</Texto>
            </View>
            <Texto suave>
              {formatoFechaHora(v.creadaEn)} · {v.formaPago}
            </Texto>
          </Tarjeta>
        ))}
      </Tarjeta>
    </Pantalla>
  );
}
