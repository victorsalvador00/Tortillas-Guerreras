import { fechaOperativa } from '@tg/shared';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { cliente, motivos, productosParaVenta } from '@/db/consultas';
import { registrarRetorno } from '@/db/operaciones';
import { leerGps } from '@/servicios/gps';
import { useSync } from '@/servicios/sincronizacion';
import { Aviso, Boton, Cantidad, Opciones, Pantalla, Tarjeta, Texto, Vacio } from '@/ui/componentes';
import { useDatos } from '@/ui/useDatos';

type Condicion = 'Vendible' | 'NoVendible';

interface PartidaRetorno {
  cantidad: number;
  condicion: Condicion;
  motivoId: string | null;
}

/**
 * Retorno: producto, cantidad (unidad base), condición y motivo. Tortilla devuelta se propone como no vendible
 * [SUP-13]; lo no vendible sale como merma en el servidor (RN-LOG-04).
 */
export default function Retorno() {
  const { clienteId } = useLocalSearchParams<{ clienteId: string }>();
  const { notificarCambio, sincronizar } = useSync();
  const datos = useDatos(() => {
    const c = cliente(clienteId);
    return { cliente: c, productos: c ? productosParaVenta(c, fechaOperativa()) : [], motivos: motivos('retorno') };
  });
  const c = datos.cliente;
  const [tipo, setTipo] = useState<'DevolucionCliente' | 'RechazoEntrega'>('DevolucionCliente');
  const [partidas, setPartidas] = useState<Record<string, PartidaRetorno>>({});
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const motivoNoVendible = datos.motivos.find((m) => m.clave === 'DEV_NO_VENDIBLE')?.id ?? datos.motivos[0]?.id ?? null;

  if (!c) return <Vacio texto="El cliente no está en tu ruta." />;

  const actualizar = (productoId: string, cambio: Partial<PartidaRetorno>, sku: string) =>
    setPartidas((s) => {
      const actual = s[productoId] ?? {
        cantidad: 0,
        // [SUP-13] La tortilla devuelta se considera no vendible.
        condicion: sku.startsWith('PT-TORT') ? 'NoVendible' : 'Vendible',
        motivoId: sku.startsWith('PT-TORT') ? motivoNoVendible : null,
      };
      const nuevo = { ...actual, ...cambio };
      if (nuevo.condicion === 'NoVendible' && !nuevo.motivoId) nuevo.motivoId = motivoNoVendible;
      if (nuevo.condicion === 'Vendible') nuevo.motivoId = null;
      return { ...s, [productoId]: nuevo };
    });

  const lista = Object.entries(partidas).filter(([, p]) => p.cantidad > 0);

  const guardar = async () => {
    setError(null);
    if (lista.length === 0) {
      setError('Captura al menos un producto.');
      return;
    }
    if (lista.some(([, p]) => p.condicion === 'NoVendible' && !p.motivoId)) {
      setError('Elige el motivo de lo no vendible.');
      return;
    }
    setGuardando(true);
    try {
      const gps = await leerGps();
      registrarRetorno({
        tipo,
        clienteId: c.id,
        ventaId: null,
        pedidoId: null,
        partidas: lista.map(([productoId, p]) => ({
          productoId,
          cantidadBase: p.cantidad,
          condicion: tipo === 'RechazoEntrega' ? 'Vendible' : p.condicion,
          motivoMermaId: tipo === 'RechazoEntrega' ? null : p.motivoId,
        })),
        gps,
      });
      notificarCambio();
      void sincronizar();
      router.back();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo guardar el retorno.');
      setGuardando(false);
    }
  };

  return (
    <Pantalla
      pie={
        <>
          {error && <Aviso tipo="rojo">{error}</Aviso>}
          <Boton texto="Guardar retorno" icono="assignment-return" grande cargando={guardando} alPresionar={guardar} />
        </>
      }
    >
      <Tarjeta>
        <Texto negrita tamano={17}>
          {c.nombre}
        </Texto>
        <Opciones
          opciones={[
            { valor: 'DevolucionCliente', texto: 'Devolución' },
            { valor: 'RechazoEntrega', texto: 'Rechazo de entrega' },
          ]}
          valor={tipo}
          alCambiar={setTipo}
        />
        <Texto suave>
          {tipo === 'DevolucionCliente'
            ? 'El cliente regresa producto. Lo vendible vuelve a la camioneta; lo no vendible es merma.'
            : 'El cliente no recibió el producto: no hay venta y sigue en la camioneta.'}
        </Texto>
      </Tarjeta>
      {datos.productos.map((p) => {
        const actual = partidas[p.productoId];
        return (
          <Tarjeta key={p.productoId}>
            <Texto negrita>
              {p.nombre} ({p.unidadBase})
            </Texto>
            <Cantidad valor={actual?.cantidad ?? 0} decimales={3} alCambiar={(v) => actualizar(p.productoId, { cantidad: v }, p.sku)} />
            {tipo === 'DevolucionCliente' && (actual?.cantidad ?? 0) > 0 && (
              <View style={{ gap: 8 }}>
                <Opciones<Condicion>
                  opciones={[
                    { valor: 'Vendible', texto: 'Vendible' },
                    { valor: 'NoVendible', texto: 'No vendible' },
                  ]}
                  valor={actual!.condicion}
                  alCambiar={(v) => actualizar(p.productoId, { condicion: v }, p.sku)}
                />
                {actual!.condicion === 'NoVendible' && (
                  <Opciones
                    opciones={datos.motivos.map((m) => ({ valor: m.id, texto: m.nombre }))}
                    valor={actual!.motivoId ?? ''}
                    alCambiar={(v) => actualizar(p.productoId, { motivoId: v }, p.sku)}
                  />
                )}
              </View>
            )}
          </Tarjeta>
        );
      })}
    </Pantalla>
  );
}
