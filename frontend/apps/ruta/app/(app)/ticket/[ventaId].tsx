import { formatoCantidad, formatoCentavos, formatoFechaHora } from '@tg/shared';
import { router, useLocalSearchParams } from 'expo-router';
import { Share, View } from 'react-native';
import QRCode from 'react-native-qrcode-svg';
import { venta } from '@/db/consultas';
import { urlTicket } from '@/servicios/config';
import { Logo } from '@/ui/Logo';
import { Aviso, Boton, Etiqueta, Pantalla, Tarjeta, Texto, Vacio, estilos } from '@/ui/componentes';
import { useDatos } from '@/ui/useDatos';

/** Texto del ticket para compartir por WhatsApp u otra app [SUP-17]. */
function textoTicket(v: NonNullable<ReturnType<typeof venta>>): string {
  const lineas = [
    'Guerreras · Por Familia Ochoa',
    `Ticket ${v.folio}`,
    formatoFechaHora(v.creadaEn),
    v.cliente?.nombre ?? '',
    '',
    ...v.partidas.map((p) => `${formatoCantidad(p.cantidad)} ${p.unidadClave} ${p.descripcion}  ${formatoCentavos(p.total)}`),
    '',
    `Total: ${formatoCentavos(v.total)} (${v.formaPago})`,
    '',
    `¿Necesitas factura? Captura tu folio en ${urlTicket(v.folio)}`,
  ];
  return lineas.join('\n');
}

/** Ticket: folio, partidas, total, leyenda y QR al portal de autofactura (`<portal>/f/<folio>`). */
export default function Ticket() {
  const { ventaId } = useLocalSearchParams<{ ventaId: string }>();
  const v = useDatos(() => venta(ventaId));
  if (!v) return <Vacio texto="El ticket ya no está en este teléfono." />;
  const url = urlTicket(v.folio);

  return (
    <Pantalla
      pie={
        <>
          <Boton texto="Compartir ticket" icono="share" grande alPresionar={() => void Share.share({ message: textoTicket(v) })} />
          <Boton texto="Volver al cliente" variante="secundario" alPresionar={() => router.dismissTo(`/clientes/${v.clienteId}`)} />
        </>
      }
    >
      {v.estadoSync === 'Rechazada' && <Aviso tipo="rojo">Rechazada por el servidor: {v.errorSync}</Aviso>}
      <Tarjeta>
        <Logo ancho={180} />
        <View style={[estilos.filaEntre, { marginTop: 8 }]}>
          <Texto negrita tamano={18}>
            {v.folio}
          </Texto>
          <Etiqueta estado={v.estadoSync} />
        </View>
        <Texto suave>{formatoFechaHora(v.creadaEn)}</Texto>
        <Texto>{v.cliente?.nombre}</Texto>
        <View style={{ height: 1, backgroundColor: '#eceef5', marginVertical: 4 }} />
        {v.partidas.map((p) => (
          <View key={p.id} style={estilos.filaEntre}>
            <Texto estilo={{ flex: 1 }}>
              {formatoCantidad(p.cantidad)} {p.unidadClave} · {p.descripcion}
            </Texto>
            <Texto>{formatoCentavos(p.total)}</Texto>
          </View>
        ))}
        <View style={{ height: 1, backgroundColor: '#eceef5', marginVertical: 4 }} />
        {v.iva + v.ieps > 0 && (
          <View style={estilos.filaEntre}>
            <Texto suave>Subtotal</Texto>
            <Texto suave>{formatoCentavos(v.subtotal)}</Texto>
          </View>
        )}
        {v.ieps > 0 && (
          <View style={estilos.filaEntre}>
            <Texto suave>IEPS</Texto>
            <Texto suave>{formatoCentavos(v.ieps)}</Texto>
          </View>
        )}
        {v.iva > 0 && (
          <View style={estilos.filaEntre}>
            <Texto suave>IVA</Texto>
            <Texto suave>{formatoCentavos(v.iva)}</Texto>
          </View>
        )}
        <View style={estilos.filaEntre}>
          <Texto negrita tamano={18}>
            Total
          </Texto>
          <Texto negrita tamano={18}>
            {formatoCentavos(v.total)}
          </Texto>
        </View>
        <Texto suave>
          {v.condicionPago} · {v.formaPago}
        </Texto>
      </Tarjeta>
      <Tarjeta>
        <Texto negrita>¿Necesitas factura?</Texto>
        <Texto suave>Escanea el código o entra a {url} con el folio y el total del ticket.</Texto>
        <View style={{ alignItems: 'center', padding: 12, backgroundColor: '#fff', borderRadius: 12 }}>
          <QRCode value={url} size={180} />
        </View>
      </Tarjeta>
    </Pantalla>
  );
}
