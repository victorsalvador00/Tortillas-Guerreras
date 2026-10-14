import { formatoCantidad, formatoCentavos } from '@tg/shared';
import { router, useLocalSearchParams } from 'expo-router';
import { useMemo, useState } from 'react';
import { View } from 'react-native';
import { cliente, parametro, productosParaVenta } from '@/db/consultas';
import { confirmarVenta, type PartidaVentaApp } from '@/db/operaciones';
import { calcularVenta, validarExistencia } from '@/dominio/ventas';
import { leerGps } from '@/servicios/gps';
import { useSync } from '@/servicios/sincronizacion';
import { Aviso, Boton, Campo, Cantidad, Opciones, Pantalla, Tarjeta, Texto, Vacio, estilos } from '@/ui/componentes';
import { useDatos } from '@/ui/useDatos';

type FormaPago = 'Efectivo' | 'Transferencia' | 'Credito';

/**
 * Nueva venta: productos con existencia de la camioneta, cantidad, precio de lista, total en centavos (RN-APP-06) y
 * forma de pago. Confirmar toma el GPS (máx. 10 s, RN-APP-03) y guarda todo en una transacción (RN-APP-01).
 */
export default function NuevaVenta() {
  const { clienteId } = useLocalSearchParams<{ clienteId: string }>();
  const { notificarCambio, sincronizar } = useSync();
  const datos = useDatos(() => {
    const c = cliente(clienteId);
    return { cliente: c, productos: c ? productosParaVenta(c.listaPrecioId) : [], permiteNegativo: parametro('ruta.permite_existencia_negativa', true) };
  });
  const c = datos.cliente;
  const [cantidades, setCantidades] = useState<Record<string, number>>({});
  const [unidades, setUnidades] = useState<Record<string, string>>({});
  const [forma, setForma] = useState<FormaPago>(c?.condicionPago === 'Credito' ? 'Credito' : 'Efectivo');
  const [buscar, setBuscar] = useState('');
  const [confirmandoFaltante, setConfirmandoFaltante] = useState(false);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const partidas = useMemo<PartidaVentaApp[]>(
    () =>
      datos.productos.flatMap((p) => {
        const cantidad = cantidades[p.productoId] ?? 0;
        if (cantidad <= 0) return [];
        const u = p.unidades.find((x) => x.unidadId === unidades[p.productoId]) ?? p.unidades.find((x) => x.predeterminada) ?? p.unidades[0];
        return [
          {
            productoId: p.productoId, unidadId: u.unidadId, unidadClave: u.clave, descripcion: p.nombre, cantidad,
            factorABase: u.factorABase, precioUnitario: u.precio ?? 0, tasaIva: p.tasaIva, tasaIeps: p.tasaIeps,
          },
        ];
      }),
    [datos.productos, cantidades, unidades],
  );
  const venta = calcularVenta(partidas);
  const existencias = useMemo(() => new Map(datos.productos.map((p) => [p.productoId, p.existencia])), [datos.productos]);
  const validacion = validarExistencia(venta.partidas, existencias, datos.permiteNegativo);
  const nombre = (id: string) => datos.productos.find((p) => p.productoId === id)?.nombre ?? '';
  const excedeCredito =
    forma === 'Credito' && c?.limiteCredito != null && Math.round(c.saldo * 100) + venta.total > Math.round(c.limiteCredito * 100);

  if (!c) return <Vacio texto="El cliente no está en tu ruta." />;

  const visibles = datos.productos.filter((p) => {
    const t = buscar.trim().toLowerCase();
    return !t || `${p.nombre} ${p.sku}`.toLowerCase().includes(t);
  });

  const confirmar = async () => {
    setError(null);
    if (partidas.length === 0) {
      setError('Agrega al menos un producto.');
      return;
    }
    if (!validacion.permitido) {
      setError('No hay existencia suficiente en la camioneta para esta venta.');
      return;
    }
    if (validacion.faltantes.length > 0 && !confirmandoFaltante) {
      setConfirmandoFaltante(true);
      return;
    }
    setGuardando(true);
    try {
      const gps = await leerGps();
      const r = confirmarVenta({
        clienteId: c.id,
        condicionPago: forma === 'Credito' ? 'Credito' : 'Contado',
        formaPago: forma,
        partidas,
        gps,
      });
      notificarCambio();
      void sincronizar();
      router.replace(`/ticket/${r.id}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo guardar la venta.');
      setGuardando(false);
    }
  };

  return (
    <Pantalla
      pie={
        <>
          <View style={estilos.filaEntre}>
            <Texto suave>
              {partidas.length} producto(s){venta.iva + venta.ieps > 0 ? ` · impuestos ${formatoCentavos(venta.iva + venta.ieps)}` : ''}
            </Texto>
            <Texto negrita tamano={22}>
              {formatoCentavos(venta.total)}
            </Texto>
          </View>
          {confirmandoFaltante && (
            <Aviso tipo="naranja">
              La camioneta quedará en negativo en: {validacion.faltantes.map((f) => nombre(f.productoId)).join(', ')}. Se generará una incidencia.
              Toca de nuevo para confirmar.
            </Aviso>
          )}
          {error && <Aviso tipo="rojo">{error}</Aviso>}
          <Boton
            texto={guardando ? 'Tomando ubicación…' : confirmandoFaltante ? 'Confirmar de todos modos' : 'Confirmar venta'}
            icono="check-circle"
            grande
            cargando={guardando}
            deshabilitado={partidas.length === 0}
            alPresionar={confirmar}
          />
        </>
      }
    >
      <Tarjeta>
        <Texto negrita tamano={17}>
          {c.nombre}
        </Texto>
        <Opciones<FormaPago>
          opciones={[
            { valor: 'Efectivo', texto: 'Efectivo' },
            { valor: 'Transferencia', texto: 'Transferencia' },
            ...(c.condicionPago === 'Credito' ? [{ valor: 'Credito' as const, texto: 'Crédito' }] : []),
          ]}
          valor={forma}
          alCambiar={setForma}
        />
        {excedeCredito && <Aviso tipo="amarillo">La venta excede el límite de crédito; se registrará con incidencia.</Aviso>}
      </Tarjeta>

      <Campo etiqueta="Buscar producto" value={buscar} onChangeText={setBuscar} />
      {visibles.length === 0 && <Vacio icono="inventory-2" texto="No hay productos con precio para este cliente. Sincroniza." />}
      {visibles.map((p) => {
        const u = p.unidades.find((x) => x.unidadId === unidades[p.productoId]) ?? p.unidades.find((x) => x.predeterminada) ?? p.unidades[0];
        const faltante = validacion.faltantes.some((f) => f.productoId === p.productoId);
        return (
          <Tarjeta key={p.productoId}>
            <View style={estilos.filaEntre}>
              <View style={{ flex: 1 }}>
                <Texto negrita>{p.nombre}</Texto>
                <Texto suave>
                  {formatoCentavos(Math.round((u.precio ?? 0) * 100))} / {u.clave} · hay {formatoCantidad(p.existencia, 3)} {p.unidadBase}
                </Texto>
              </View>
            </View>
            {p.unidades.length > 1 && (
              <Opciones
                opciones={p.unidades.map((x) => ({ valor: x.unidadId, texto: x.clave }))}
                valor={u.unidadId}
                alCambiar={(v) => setUnidades((s) => ({ ...s, [p.productoId]: v }))}
              />
            )}
            <Cantidad
              valor={cantidades[p.productoId] ?? 0}
              decimales={u.decimales}
              alCambiar={(v) => {
                setConfirmandoFaltante(false);
                setCantidades((s) => ({ ...s, [p.productoId]: v }));
              }}
            />
            {faltante && <Texto estilo={{ color: '#a64200' }}>Más de lo que hay en la camioneta.</Texto>}
          </Tarjeta>
        );
      })}
    </Pantalla>
  );
}
