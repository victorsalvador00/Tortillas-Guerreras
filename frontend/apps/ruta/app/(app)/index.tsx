import { fechaOperativa, formatoCentavos, formatoCantidad, formatoFechaHora, formatoFechaLarga } from '@tg/shared';
import { router } from 'expo-router';
import { View } from 'react-native';
import { estadoDispositivo, resumenDia } from '@/db/consultas';
import { useSesion } from '@/servicios/contextoSesion';
import { useSync } from '@/servicios/sincronizacion';
import { Aviso, Boton, Etiqueta, Indicador, Pantalla, Tarjeta, Texto, estilos } from '@/ui/componentes';
import { useDatos } from '@/ui/useDatos';

/** Mi día: ruta, fecha, carga, totales, efectivo esperado, pendientes y botón Sincronizar (06). */
export default function MiDia() {
  const { perfil, salir } = useSesion();
  const { sincronizar, sincronizando, ultimo, conRed } = useSync();
  const hoy = fechaOperativa();
  const { resumen, estado } = useDatos(() => ({ resumen: resumenDia(hoy), estado: estadoDispositivo() }));

  return (
    <Pantalla alRefrescar={sincronizar} refrescando={sincronizando}>
      <Tarjeta>
        <View style={estilos.filaEntre}>
          <View style={{ flex: 1 }}>
            <Texto negrita tamano={18}>
              {estado.rutaNombre ?? 'Sin ruta asignada'} {estado.rutaCodigo ? `· ${estado.rutaCodigo}` : ''}
            </Texto>
            <Texto suave>
              {perfil?.nombreCompleto} · {estado.codigo ?? 'sin dispositivo'}
            </Texto>
            <Texto suave>{formatoFechaLarga(hoy)}</Texto>
          </View>
          <Etiqueta texto={conRed ? 'En línea' : 'Sin red'} color={conRed ? 'verde' : 'naranja'} />
        </View>
      </Tarjeta>

      {!estado.rutaId && <Aviso tipo="amarillo">Sincroniza para descargar tu ruta, clientes, precios y carga del día.</Aviso>}
      {ultimo?.error && <Aviso tipo={ultimo.requiereLogin ? 'amarillo' : 'rojo'}>{ultimo.error}</Aviso>}
      {resumen.rechazadas > 0 && (
        <Tarjeta alPresionar={() => router.push('/pendientes')}>
          <Aviso tipo="rojo">{resumen.rechazadas} operación(es) rechazada(s). Toca para ver el motivo.</Aviso>
        </Tarjeta>
      )}

      <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 12 }}>
        <Indicador icono="local-shipping" valor={formatoCantidad(resumen.cargado, 2)} titulo="Carga recibida" />
        <Indicador icono="receipt" valor={String(resumen.ventas)} titulo="Ventas de hoy" />
        <Indicador icono="payments" valor={formatoCentavos(resumen.totalVendido)} titulo="Total vendido" />
        <Indicador icono="account-balance-wallet" valor={formatoCentavos(resumen.efectivoEsperado)} titulo="Efectivo esperado" tono="verde" />
      </View>

      <Tarjeta titulo="Sincronización" accion={<Etiqueta texto={`${resumen.pendientes} por enviar`} color={resumen.pendientes ? 'amarillo' : 'verde'} />}>
        <Texto suave>Último: {estado.ultimoSync ? formatoFechaHora(estado.ultimoSync) : 'nunca'}</Texto>
        <Boton texto={sincronizando ? 'Sincronizando…' : 'Sincronizar'} icono="sync" grande cargando={sincronizando} alPresionar={() => void sincronizar()} />
      </Tarjeta>

      <Boton texto="Clientes de hoy" icono="groups" grande alPresionar={() => router.push('/clientes')} />
      <Boton texto="Inventario de camioneta" icono="inventory" variante="secundario" alPresionar={() => router.push('/inventario')} />
      <Boton texto="Pendientes" icono="pending-actions" variante="secundario" alPresionar={() => router.push('/pendientes')} />
      <Boton texto="Cerrar sesión" variante="texto" alPresionar={salir} />
    </Pantalla>
  );
}
